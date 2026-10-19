using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TransparentChatFilter;

internal static class YouTubeCredentialStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransparentChatFilter",
        "youtube-refresh-token.dat");

    public static string? LoadRefreshToken()
    {
        if (!File.Exists(TokenPath))
        {
            return null;
        }

        var encrypted = Convert.FromBase64String(File.ReadAllText(TokenPath));
        var clearText = Transform(encrypted, protect: false);
        try
        {
            return Encoding.UTF8.GetString(clearText);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearText);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public static void SaveRefreshToken(string refreshToken)
    {
        var clearText = Encoding.UTF8.GetBytes(refreshToken);
        try
        {
            var encrypted = Transform(clearText, protect: true);
            try
            {
                var directory = Path.GetDirectoryName(TokenPath)
                    ?? throw new InvalidOperationException("O caminho das credenciais do YouTube não é válido.");
                Directory.CreateDirectory(directory);
                File.WriteAllText(TokenPath, Convert.ToBase64String(encrypted));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearText);
        }
    }

    public static void DeleteRefreshToken()
    {
        if (File.Exists(TokenPath))
        {
            File.Delete(TokenPath);
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputPointer = Marshal.AllocHGlobal(input.Length);
        Marshal.Copy(input, 0, inputPointer, input.Length);
        var inputBlob = new DataBlob { Length = input.Length, Data = inputPointer };
        var outputBlob = default(DataBlob);

        try
        {
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, "Transparent Chat Filter YouTube credentials", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outputBlob);
            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível proteger ou ler as credenciais do YouTube.");
            }

            var result = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, result, 0, outputBlob.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Length], 0, inputPointer, input.Length);
            Marshal.FreeHGlobal(inputPointer);
            if (outputBlob.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[outputBlob.Length], 0, outputBlob.Data, outputBlob.Length);
                LocalFree(outputBlob.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
