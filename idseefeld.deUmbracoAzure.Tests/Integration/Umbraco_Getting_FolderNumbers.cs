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
        public void Starts_At_1000_On_Each_Recycle()
        {
            for(var i = 1000; i<1002; i++)
                Sut.AddFile(i + "/test.dat", CreateTestStream());

            var number = MediaSubfolderCounter_Adaption();
            Assert.AreEqual(1000, number);
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
