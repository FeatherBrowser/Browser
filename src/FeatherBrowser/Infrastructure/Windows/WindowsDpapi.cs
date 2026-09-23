using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace FeatherBrowser.Infrastructure.Windows;

internal static class WindowsDpapi
{
    private const uint CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static string Protect(string plaintext)
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes(plaintext);
        DataBlob input = MakeInputBlob(inputBytes);
        DataBlob output = default;
        try
        {
            if (!CryptProtectData(ref input, "Feather Browser password", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect the password.");
            return Convert.ToBase64String(CopyOutput(output));
        }
        finally
        {
            if (input.pbData != IntPtr.Zero)
                Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero)
                LocalFree(output.pbData);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(inputBytes);
        }
    }

    public static string Unprotect(string protectedValue)
    {
        byte[] inputBytes = Convert.FromBase64String(protectedValue);
        DataBlob input = MakeInputBlob(inputBytes);
        DataBlob output = default;
        byte[]? clearBytes = null;
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not unlock this saved password.");
            clearBytes = CopyOutput(output);
            return Encoding.UTF8.GetString(clearBytes);
        }
        finally
        {
            if (input.pbData != IntPtr.Zero)
                Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero)
                LocalFree(output.pbData);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(inputBytes);
            if (clearBytes is not null)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private static DataBlob MakeInputBlob(byte[] bytes)
    {
        IntPtr ptr = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        if (bytes.Length > 0)
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return new DataBlob { cbData = bytes.Length, pbData = ptr };
    }

    private static byte[] CopyOutput(DataBlob blob)
    {
        byte[] bytes = new byte[blob.cbData];
        if (blob.cbData > 0)
            Marshal.Copy(blob.pbData, bytes, 0, blob.cbData);
        return bytes;
    }
}
