using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BTrader.Python;
using System.Linq;

namespace BTrader.Python.Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            var processDetails = "x84";
            if (Environment.Is64BitProcess)
            {
                processDetails = "x64";
            }

            Console.WriteLine($"Running as {processDetails} process");

            var test = new PythonSessionTests();
            test.CreateSession();

            Console.ReadLine();
        }
    }

    [TestClass]
    public class PythonSessionTests
    {
        [TestMethod]
        public void CreateSession()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var pythonHome = @"C:\Users\adam\Work\BTrader\BTrader.Python\python3.6.1_x64\runtime\";
            var pythonPath = $@"{pythonHome};{pythonHome}python36.zip;{pythonHome}\Lib\site-packages;{exeDir}";
            var pythonSession = new PythonSession(pythonHome, pythonPath, new Log());
            var moduleFileName = "testfunctions";
            //pythonSession.LoadModule(moduleFileName);
            //var result = pythonSession.CallModuleFunction(moduleFileName, "return_input", "test");
            pythonSession.LoadModule(moduleFileName);
            var insensityRatio = pythonSession.CallModuleFunction<float>(moduleFileName, "get_intensity_ratio", 1.0, new[] { 1.0, 2.0, 4.1 }.ToList(), new[] { 2.1, 2.2, 2.3, 4.1 }.ToList()).Result;
            insensityRatio = pythonSession.CallModuleFunction<float>(moduleFileName, "get_intensity_ratio", 1.0, new[] { 1.0, 2.0, 4.1 }.ToList(), new[] { 2.1, 2.2, 2.3, 4.1, 5.6 }.ToList()).Result;
            pythonSession.Dispose();
        }
    }
}
