namespace ProWeighSuitePlatforms.Helpers
{
    public static class ZplTestHelper
    {
        /// <summary>
        /// Returns a simple ZPL test label for verifying ZT231 printer connectivity.
        /// </summary>
        public static string GetTestZpl()
        {
            return "^XA^FO50,50^ADN,36,20^FDPrinter Test^FS^FO50,100^ADN,18,10^FDZT231 Connected^FS^XZ";
        }
    }
}
