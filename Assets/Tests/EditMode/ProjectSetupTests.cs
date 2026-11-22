using System.IO.Ports;
using System.Linq;
using NUnit.Framework;
using StressTest.Core;

namespace StressTest.Tests
{
    public class ProjectSetupTests
    {
        [Test]
        public void SerialPortApi_IsAvailable()
        {
            // 需要 API Compatibility Level = .NET Framework 才能編譯與執行
            Assert.That(SerialPort.GetPortNames(), Is.Not.Null);
        }

        [Test]
        public void CoreAssembly_DoesNotReferenceUnityEngine()
        {
            var references = typeof(AssemblyMarker).Assembly
                .GetReferencedAssemblies()
                .Select(a => a.Name);

            Assert.That(references, Has.None.StartsWith("UnityEngine"));
            Assert.That(references, Has.None.StartsWith("UnityEditor"));
        }
    }
}
