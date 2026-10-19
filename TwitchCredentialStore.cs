using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransparentChatFilter;

internal static class TwitchCredentialStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly string TokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransparentChatFilter",
        "twitch-token.dat");

    public static TwitchCredentials? LoadCredentials(string? legacyUsername = null)
    {
        if (!File.Exists(TokenPath))
        {
            return null;
        }

        var encrypted = Convert.FromBase64String(File.ReadAllText(TokenPath));
        var clearText = Transform(encrypted, protect: false);
        try
        {
            try
            {
                return JsonSerializer.Deserialize<TwitchCredentials>(clearText);
            }
            catch (JsonException)
            {
                var legacyToken = Encoding.UTF8.GetString(clearText);
                return string.IsNullOrWhiteSpace(legacyToken)
                    ? null
                    : new TwitchCredentials(legacyUsername ?? string.Empty, legacyToken, string.Empty, DateTimeOffset.MinValue);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearText);
        }
    }

    public static void SaveCredentials(TwitchCredentials credentials)
    {
        var clearText = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try
        {
            var encrypted = Transform(clearText, protect: true);
            try
            {
                var directory = Path.GetDirectoryName(TokenPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new InvalidOperationException("O caminho das credenciais da Twitch não é válido.");
                }

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

    private static byte[] Transform(byte[] input, bool protect)
    {
        var inputPointer = Marshal.AllocHGlobal(input.Length);
        Marshal.Copy(input, 0, inputPointer, input.Length);
        var inputBlob = new DataBlob { Length = input.Length, Data = inputPointer };
        var outputBlob = default(DataBlob);

        try
        {
            var succeeded = protect
                ? CryptProtectData(ref inputBlob, "Transparent Chat Filter Twitch credentials", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref outputBlob);
            if (!succeeded)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível proteger ou ler as credenciais da Twitch.");
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
