﻿/*
This code was inspired by Johannes Mueller
*/

using idseefeld.de.UmbracoAzure.Infrastructure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.IO;

namespace idseefeld.de.UmbracoAzure {
	public class AzureBlobFileSystem : IFileSystem {
	    private readonly CloudBlobClient cloudBlobClient;
	    private readonly CloudBlobContainer mediaContainer;
	    private readonly Dictionary<string, CloudBlockBlob> cachedBlobs = new Dictionary<string, CloudBlockBlob>();
	    private readonly ILogger logger;
        private readonly string containerUrl;
	    private readonly Uri containerUri;

		public AzureBlobFileSystem(
			string containerName,
			string rootUrl,
			string connectionString)
        : this(
            new LogAdapter(), 
            CloudStorageAccount.Parse(connectionString),
            containerName,
            rootUrl
        )
		{
		}

	    internal AzureBlobFileSystem(
            ILogger logger,
            CloudStorageAccount account,
			string containerName,
			string rootUrl)
	    {
	        cloudBlobClient = account.CreateCloudBlobClient();
			mediaContainer = CreateContainer(containerName, BlobContainerPublicAccessType.Blob);
			containerUrl = rootUrl + containerName + "/";
            containerUri = new Uri(containerUrl);
        
            this.logger = logger;
        }

	    public void AddFile(string path, Stream stream, bool overrideIfExists)
		{
			var fileExists = FileExists(path);
			if (fileExists && !overrideIfExists)
			{
				logger.Warn<AzureBlobFileSystem>(string.Format("A file at path '{0}' already exists", path));
			}
			AddFile(path, stream);
		}

		public void AddFile(string path, Stream stream)
		{
			if (!path.StartsWith(containerUrl))
			{
				path = containerUrl + path.Replace('\\', '/');
			}
			UploadFileToBlob(path, stream);
		}

		public void DeleteDirectory(string path, bool recursive)
		{
			if (!DirectoryExists(path))
				return;

			DeleteDirectoryInBlob(path);
		}

		public void DeleteDirectory(string path)
		{
			DeleteDirectory(path, false);
		}

		public void DeleteFile(string path)
		{
			DeleteFileFromBlob(path);
		}

		public bool DirectoryExists(string path)
		{
			return DirectoryExistsInBlob(path);
		}

		public bool FileExists(string path)
		{
			bool rVal = FileExistsInBlob(path);
			return rVal;
		}

		public DateTimeOffset GetCreated(string path)
		{
			return DirectoryExists(path)
				? Directory.GetCreationTimeUtc(GetFullPath(path))
				: File.GetCreationTimeUtc(GetFullPath(path));
		}

		public IEnumerable<string> GetDirectories(string path)
		{
			return GetDirectoriesFromBlob(path);
		}

		public IEnumerable<string> GetFiles(string path, string filter)
		{
			return GetFilesFromBlob(path, filter);
		}

		public IEnumerable<string> GetFiles(string path)
		{
			return GetFiles(path, "*.*");
		}

		public string GetFullPath(string path)
		{
			string rVal = !path.StartsWith(containerUrl)
				 ? Path.Combine(containerUrl, path)
				 : path;
			return rVal;
		}

		public DateTimeOffset GetLastModified(string path)
		{
			return GetLastModifiedDateOfBlob(path);
		}

		public string GetRelativePath(string fullPathOrUrl)
		{
		    var uri = new Uri(fullPathOrUrl, UriKind.RelativeOrAbsolute);
		    if (uri.IsAbsoluteUri)
		        return uri.AbsolutePath.TrimStart(containerUri.AbsolutePath).TrimEnd("/");
            return fullPathOrUrl.TrimEnd("/");
		}

		public string GetUrl(string path)
		{
			string rVal = path;
			if (!path.StartsWith("http"))
			{
				rVal = containerUrl.TrimEnd("/") + "/" + path
					 .TrimStart(Path.DirectorySeparatorChar)
					 .Replace(Path.DirectorySeparatorChar, '/')
					 .TrimEnd("/");
			}
			return rVal;
		}

		public Stream OpenFile(string path)
		{
			Stream rVal = DownloadFileFromBlob(path);
			return rVal;
		}

		private CloudBlobContainer CreateContainer(string containerName, BlobContainerPublicAccessType accessType)
		{
			var container = GetContainer(containerName);
			container.CreateIfNotExists();
			container.SetPermissions(new BlobContainerPermissions { PublicAccess = accessType });
			return container;
		}

		private CloudBlobDirectory CreateDirectories(string[] paths)
		{
			var current = mediaContainer.GetDirectoryReference(paths[0]);
			for (var i = 1; i < paths.Count(); i++)
			{
				current = current.GetSubdirectoryReference(paths[i]);
			}
			return current;
		}
		private void DeleteDirectoryInBlob(string path)
		{
			var blobs = GetDirectoryBlobs(path);
			foreach (var item in blobs)
			{
				if (item is CloudBlockBlob || item.GetType().BaseType == typeof(CloudBlockBlob))
				{
					((CloudBlockBlob)item).DeleteIfExists();
				}
			}
		}

		private void DeleteFileFromBlob(string path)
		{
			path = path.Replace('\\', '/');
			try
			{
				var blockBlob = GetBlockBlob(MakeUri(path));
				blockBlob.Delete();
			}
			catch (Exception ex)
			{
				logger.Error<AzureBlobFileSystem>("Delete File Error: " + path, ex);
			}
		}

		private bool DirectoryExistsInBlob(string path)
		{
			var blobs = GetDirectoryBlobs(path);
			bool rVal = blobs.Any();
			return rVal;
		}
		private IEnumerable<IListBlobItem> GetDirectoryBlobs(string path, bool useFlatBlobListing = true)
		{
			path = path.Replace('\\', '/').TrimEnd('/');
			string dir = path.Substring(path.LastIndexOf('/') + 1);
			return mediaContainer.ListBlobs(dir, useFlatBlobListing);
		}

		private Stream DownloadFileFromBlob(string path)
		{
			var blockBlob = GetBlockBlob(MakeUri(path));
			var fileStream = new MemoryStream();
			blockBlob.DownloadToStream(fileStream);
			return fileStream;
		}

		private bool FileExistsInBlob(string path)
		{
			try
			{
			    var blob = GetBlockBlob(MakeUri(path));
				return blob.Exists();
			}
			catch (Exception ex)
			{
				return false;
			}
		}

		private CloudBlockBlob GetBlockBlob(Uri uri)
		{
			CloudBlockBlob blockBlob;
		    if (!cachedBlobs.TryGetValue(uri.ToString(), out blockBlob))
		    {
		        blockBlob = cloudBlobClient.GetBlobReferenceFromServer(uri) as CloudBlockBlob;
                cachedBlobs.Add(uri.ToString(), blockBlob);
		    }
			if (blockBlob == null)
			{
				logger.Warn<AzureBlobFileSystem>("File not found in BLOB: " + uri.AbsoluteUri);
			}
			return blockBlob;
		}

		private CloudBlobContainer GetContainer(string containerName)
		{
			return cloudBlobClient.GetContainerReference(containerName);
		}

		private IEnumerable<string> GetDirectoriesFromBlob(string path)
		{
		    if (!path.EndsWith("/") && path.Length > 0)
		        path += "/";
            var blobs = mediaContainer.ListBlobs(path);
            return blobs.Where(i => i is CloudBlobDirectory).Select(cd => GetRelativePath(cd.Uri.ToString()));

            //see: https://github.com/idseefeld/UmbracoAzureBlobStorage/issues/1 by stefana99
            //var blobs = mediaContainer.ListBlobs(path);
            //return blobs.Where(i => i is CloudBlobDirectory).Select(cd => cd.Uri.Segments[2].Split('/')[0].ToString());
		}

		private IEnumerable<string> GetFilesFromBlob(string path, string filter)
		{
			//TODO: Filter einbinden.
			var blobs = mediaContainer.ListBlobs(path);
			return blobs.Where(i => i is CloudBlockBlob).Select(cd =>
				{
					var cloudBlockBlob = cd as CloudBlockBlob;
					//Filter vielleicht über den Namen.
					return cloudBlockBlob.Name;
				});
		}

		private DateTimeOffset GetLastModifiedDateOfBlob(string path)
		{
			var blob = GetBlockBlob(MakeUri(path));
			var lastmodified = blob.Properties.LastModified;
			return lastmodified.GetValueOrDefault();
		}

		private string MakePath(string path)
		{
			string rVal = path;
			if (!path.StartsWith("http"))
				rVal = containerUrl + path.Replace('\\', '/');
			return rVal;
		}

		private Uri MakeUri(string path)
		{
			return new Uri(MakePath(path));
		}

		private void UploadFileToBlob(string fileUrl, Stream fileStream)
		{
			string name = fileUrl.Substring(fileUrl.LastIndexOf('/') + 1);
			var dirPart = fileUrl.Substring(0, fileUrl.LastIndexOf('/'));
			dirPart = dirPart.Substring(dirPart.LastIndexOf('/') + 1);
			var directory = CreateDirectories(dirPart.Split('/'));
			var blockBlob = directory.GetBlockBlobReference(name);
            if (fileStream.CanSeek)
            {
                fileStream.Seek(0, SeekOrigin.Begin);
            }
			blockBlob.UploadFromStream(fileStream);
		}
	}
}