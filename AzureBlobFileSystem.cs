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
		private string _rootPath;
		private string _rootUrl;
		private CloudBlobClient cloudBlobClient;
		private CloudStorageAccount cloudStorageAccount;
		private CloudBlobContainer mediaContainer;
        private Dictionary<string, string> mimeTypes;
	    private readonly Dictionary<string, CloudBlockBlob> cachedBlobs = new Dictionary<string, CloudBlockBlob>();
	    private readonly ILogger logger;

		public AzureBlobFileSystem(
			string containerName,
			string rootUrl,
			string connectionString)
		{
            Init(containerName, rootUrl, connectionString, null);
            logger = new LogAdapter();
		}
        public AzureBlobFileSystem(
            string containerName,
            string rootUrl,
            string connectionString,
            string mimetypes)
        {
            Init(containerName, rootUrl, connectionString, mimetypes);
            logger = new LogAdapter();
        }
        private void Init(string containerName, string rootUrl, string connectionString, string mimetypes)
        {
            cloudStorageAccount = CloudStorageAccount.Parse(connectionString);
            cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
            mediaContainer = CreateContainer(containerName, BlobContainerPublicAccessType.Blob);
            RootUrl = rootUrl + containerName + "/";
            RootPath = "/";
            if (mimetypes != null)
            {
                var pairs = mimetypes.Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                this.mimeTypes = new Dictionary<string, string>();
                foreach (var pair in pairs)
                {
                    string[] type = pair.Split("|".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    if (type.Length == 2)
                    {
                        this.mimeTypes.Add(type[0], type[1]);
                    }
                }
            }
        }

	    internal AzureBlobFileSystem(
            ILogger logger,
            CloudStorageAccount account,
			string containerName,
			string rootUrl)
	    {
            cloudStorageAccount = account;
			cloudBlobClient = cloudStorageAccount.CreateCloudBlobClient();
			mediaContainer = CreateContainer(containerName, BlobContainerPublicAccessType.Blob);
			RootUrl = rootUrl + containerName + "/";
			RootPath = "/";
        
            this.logger = logger;
        }

		private string RootPath
		{
			get { return _rootPath; }
			set { _rootPath = value; }
		}

		private string RootUrl
		{
			get { return _rootUrl; }
			set { _rootUrl = value; }
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
			if (!path.StartsWith(RootUrl))
			{
				path = RootUrl + path.Replace('\\', '/');
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
            var rVal = GetFiles(path, "*.*");
            return rVal;
		}

		public string GetFullPath(string path)
		{
			string rVal = !path.StartsWith(RootUrl)
				 ? Path.Combine(RootUrl, path)
				 : path;
			return rVal;
		}

		public DateTimeOffset GetLastModified(string path)
		{
			return GetLastModifiedDateOfBlob(path);
		}

		public string GetRelativePath(string fullPathOrUrl)
		{
			var relativePath = fullPathOrUrl;
			if (!fullPathOrUrl.StartsWith("http"))
			{
				fullPathOrUrl
				 .TrimStart(_rootUrl)
				 .Replace('/', Path.DirectorySeparatorChar)
				 .TrimStart(Path.DirectorySeparatorChar);
			}
			return relativePath;
		}

		public string GetUrl(string path)
		{
			string rVal = path;
			if (!path.StartsWith("http"))
			{
				rVal = RootUrl.TrimEnd("/") + "/" + path
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
				current = current.GetDirectoryReference(paths[i]);
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
            var blobs = mediaContainer.ListBlobs(path);
            //always get last segment for media sub folder simulation
            var rVal = blobs.Where(i => i is CloudBlobDirectory).Select(cd => cd.Uri.Segments[cd.Uri.Segments.Length - 1].Split('/')[0]);
            return rVal;
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
				rVal = RootUrl + path.Replace('\\', '/');
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
            string contentType = GetMimeType(name);
            if (!String.IsNullOrEmpty(contentType))
            {
                blockBlob.Properties.ContentType = contentType;
                blockBlob.SetProperties();
            }
		}

        private string GetMimeType(string name)
        {
            string rVal = null;
            string ext = name.Substring(name.LastIndexOf('.') + 1).ToLower();
            switch (ext)
            {
                case "jpg":
                case "jpeg":
                    rVal = "image/jpeg";
                    break;
                case "png":
                    rVal = "image/png";
                    break;
                case "gif":
                    rVal = "image/gif";
                    break;
                case "pdf":
                    rVal = "application/pdf";
                    break;
                case "air":
                    rVal = "application/vnd.adobe.air-application-installer-package+zip";
                    break;
                default:
                    if (this.mimeTypes != null)
                    {
                        var type = this.mimeTypes.Where(t => t.Key.Equals(ext)).FirstOrDefault();
                        if (!String.IsNullOrEmpty(type.Value))
                        {
                            rVal = type.Value;
                        }
                    }
                    break;
            }
            return rVal;
        }
	}
}