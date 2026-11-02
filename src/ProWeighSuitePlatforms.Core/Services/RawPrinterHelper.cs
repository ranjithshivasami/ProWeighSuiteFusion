using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ProWeighSuitePlatforms.Services
{
    /// <summary>
    /// Helper class to send raw ZPL data directly to a printer
    /// </summary>
    public class RawPrinterHelper
    {
        // Structure and API declarations for raw printing
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public class DOCINFOA
        {
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDocName = string.Empty;
            [MarshalAs(UnmanagedType.LPStr)]
            public string? pOutputFile;
            [MarshalAs(UnmanagedType.LPStr)]
            public string pDataType = string.Empty;
        }

        [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

        [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
        public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

        /// <summary>
        /// Send a string of ZPL commands to the printer
        /// </summary>
        public static bool SendStringToPrinter(string printerName, string zplString)
        {
            IntPtr hPrinter = IntPtr.Zero;
            DOCINFOA di = new DOCINFOA();
            bool bSuccess = false;

            di.pDocName = "ZPL Label";
            di.pOutputFile = null;
            di.pDataType = "RAW";

            // Convert string to bytes using ASCII encoding (ZPL is ASCII-based). Fall back to default if needed.
            byte[] bytes = Encoding.ASCII.GetBytes(zplString);

            IntPtr pUnmanagedBytes = IntPtr.Zero;

            try
            {
                // Open the printer
                if (OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero))
                {
                    // Start a document
                    if (StartDocPrinter(hPrinter, 1, di))
                    {
                        // Start a page
                        if (StartPagePrinter(hPrinter))
                        {
                            // Allocate unmanaged memory and copy bytes
                            pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                            Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                            bSuccess = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out _);

                            EndPagePrinter(hPrinter);
                        }
                        EndDocPrinter(hPrinter);
                    }
                    ClosePrinter(hPrinter);
                }
            }
            finally
            {
                if (pUnmanagedBytes != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }

            return bSuccess;
        }

        /// <summary>
        /// Send a file containing ZPL commands to the printer
        /// </summary>
        public static bool SendFileToPrinter(string printerName, string fileName)
        {
            // Read file contents
            using (FileStream fs = new FileStream(fileName, FileMode.Open))
            {
                using (BinaryReader br = new BinaryReader(fs))
                {
                    byte[] bytes = br.ReadBytes((int)fs.Length);
                    IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                    IntPtr hPrinter = new IntPtr(0);
                    DOCINFOA di = new DOCINFOA();
                    bool bSuccess = false;

                    di.pDocName = "ZPL Label from File";
                    di.pOutputFile = null;
                    di.pDataType = "RAW";

                    if (OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero))
                    {
                        if (StartDocPrinter(hPrinter, 1, di))
                        {
                            if (StartPagePrinter(hPrinter))
                            {
                                bSuccess = WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out _);
                                EndPagePrinter(hPrinter);
                            }
                            EndDocPrinter(hPrinter);
                        }
                        ClosePrinter(hPrinter);
                    }

                    Marshal.FreeCoTaskMem(pUnmanagedBytes);
                    return bSuccess;
                }
            }
        }
    }
}
