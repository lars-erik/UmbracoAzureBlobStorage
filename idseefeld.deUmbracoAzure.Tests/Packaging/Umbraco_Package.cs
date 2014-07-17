using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;

namespace idseefeld.de.UmbracoAzure.Tests.Packaging
{
    [TestFixture]
    public class Umbraco_Package
    {
        private ZipOutputStream zipStream;

        [Test]
        public void Create()
        {
            const string sourceRoot = @"..\..\..\";
            var packageXmlPath = Path.Combine(sourceRoot, "package.xml");
            var version = typeof(AzureBlobFileSystem).Assembly.GetName().Version;
            var zipName = "AzureStorage_" + version.Major + "." + version.Minor + "." + version.MajorRevision + ".zip";
            var outputPath = Path.Combine(sourceRoot, zipName);
            var xdoc = XDocument.Load(packageXmlPath);
            var files = xdoc.XPathSelectElements("//file");
            var zipFolderName = new Guid("175257BB-C34B-42FC-B955-7D15820E5337").ToString();

            using (var fileStream = File.Create(outputPath))
            {
                zipStream = new ZipOutputStream(fileStream);
                zipStream.SetLevel(5); //0-9, 9 being the highest level of compression

                var packageXmlTargetPath = Path.Combine(zipFolderName, "package.xml");
                WriteEntry(packageXmlPath, packageXmlTargetPath);

                foreach (var file in files)
                {
                    var fileName = file.Element("orgName").Value;

                    var sourcePath = Path.Combine(
                        Path.Combine(
                            sourceRoot, 
                            file.Element("orgPath").Value.TrimStart('/').Replace("/", "\\")
                        ),
                        fileName
                    );

                    var targetPath = Path.Combine(zipFolderName, fileName);

                    WriteEntry(sourcePath, targetPath);
                }

                zipStream.IsStreamOwner = true;
                zipStream.Close();
            }
        }

        private void WriteEntry(string sourcePath, string targetPath)
        {
            var entry = new ZipEntry(targetPath);
            var buffer = new byte[4096];
            entry.DateTime = File.GetLastWriteTime(sourcePath);
            entry.Size = new FileInfo(sourcePath).Length;
            zipStream.PutNextEntry(entry);
            using (var reader = File.OpenRead(sourcePath))
            {
                StreamUtils.Copy(reader, zipStream, buffer);
            }
            zipStream.CloseEntry();
        }
    }
}
