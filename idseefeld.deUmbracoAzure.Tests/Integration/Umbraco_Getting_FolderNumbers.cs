using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace idseefeld.de.UmbracoAzure.Tests.Integration
{
    [TestFixture]
    public class Umbraco_Getting_FolderNumbers : AzureBlobFileSystemTestBase
    {
        [Test]
        public void Returns_Biggest_Number_Of_Root_Directories()
        {
            Sut.AddFile("12345/test.dat", CreateTestStream());

            var number = MediaSubfolderCounter_Adaption();
            Assert.AreEqual(12345, number);
        }

        [Test]
        public void Starts_At_1000_When_No_Numeric_Folder_Names()
        {
            Sut.AddFile("abc/test.dat", CreateTestStream());
            var number = MediaSubfolderCounter_Adaption();
            Assert.AreEqual(1000, number);
        }

        [Test]
        public void Ignores_Non_Numeric_Folders()
        {
            Sut.AddFile("abc/test.dat", CreateTestStream());
            Sut.AddFile("1234/test.dat", CreateTestStream());
            Sut.AddFile("1235/test.dat", CreateTestStream());
            Sut.AddFile("cdef/test.dat", CreateTestStream());
            var number = MediaSubfolderCounter_Adaption();
            Assert.AreEqual(1235, number);
        }

        // Copied from Umbraco.Core.Media.MediaSubfolderCounter
        private long MediaSubfolderCounter_Adaption()
        {
            var folders = new List<long>();
            var fs = Sut; // FileSystemProviderManager.Current.GetFileSystemProvider<MediaFileSystem>();
            var directories = fs.GetDirectories("");
            foreach (var directory in directories)
            {
                long dirNum;
                if (long.TryParse(directory, out dirNum))
                {
                    folders.Add(dirNum);
                }
            }
            var last = folders.OrderBy(x => x).LastOrDefault();
            if(last != default(long))
                return last; // _numberedFolder =
            return 1000; // private field
        }

    }
}
