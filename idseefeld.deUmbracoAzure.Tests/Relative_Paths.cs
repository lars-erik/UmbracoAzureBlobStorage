using NUnit.Framework;

namespace idseefeld.de.UmbracoAzure.Tests
{
    [TestFixture]
    public class Relative_Paths : AzureBlobFileSystemTestBase
    {
        [Test]
        [TestCase("1000")]
        [TestCase("1000/dill")]
        public void From_Full_Directory_Url(string expectedRelativePath)
        {
            Sut.AddFile(expectedRelativePath + "/test.dat", CreateTestStream());
            Assert_Relative_From_Full(expectedRelativePath);
        }

        [Test]
        [TestCase("1000/test.dat")]
        [TestCase("1000/dill/test.dat")]
        public void From_Full_File_Url(string expectedRelativePath)
        {
            Sut.AddFile(expectedRelativePath, CreateTestStream());
            Assert_Relative_From_Full(expectedRelativePath);
        }

        private void Assert_Relative_From_Full(string expectedRelativePath)
        {
            var fullPath = Sut.GetFullPath(expectedRelativePath);
            var actualRelative = Sut.GetRelativePath(fullPath);

            Assert.That(actualRelative, Is.Not.EqualTo(fullPath));
            Assert.That(actualRelative, Is.EqualTo(expectedRelativePath));
        }

        [Test]
        [TestCase("1000/test.dat")]
        [TestCase("1000/dill/test.dat")]
        public void From_Relative_FilePath(string expectedRelativePath)
        {
            Sut.AddFile(expectedRelativePath, CreateTestStream());
            var actualRelative = Sut.GetRelativePath(expectedRelativePath);
            Assert.That(actualRelative, Is.EqualTo(expectedRelativePath));
        }

        [Test]
        public void From_Directory_Path_With_Trailing_Slash_Removes_Trailing()
        {
            const string expectedRelativePath = "1000";
            Sut.AddFile(expectedRelativePath + "/test.dat", CreateTestStream());
            var actualRelative = Sut.GetRelativePath(expectedRelativePath + "/");
            Assert.That(actualRelative, Is.EqualTo(expectedRelativePath));
        }

        [Test]
        public void From_Full_Url_With_Secure_Scheme()
        {
            const string expectedRelativePath = "1000/test.dat";
            Sut.AddFile(expectedRelativePath, CreateTestStream());
            var fullUrl = Sut.GetFullPath(expectedRelativePath);
            var httpsUrl = "https" + fullUrl.Substring(4);
            var relative = Sut.GetRelativePath(httpsUrl);
            Assert.That(relative, Is.EqualTo(expectedRelativePath));
        }
    }
}
