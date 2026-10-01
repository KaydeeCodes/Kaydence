using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kaydence.Services;

// I seal and open my diary files with AES-GCM, every sealed file starts with KDE1 so I can tell it apart from a plain one
public static class CryptoService
{
    private static readonly byte[] Magic = "KDE1"u8.ToArray();
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // I hold the diary key in memory only while Kaydence is running
    public static byte[]? Key { get; set; }

    // I only seal new writes when the diary is in encrypted mode
    public static bool SealWrites { get; set; }

    public static bool IsSealed(byte[] data) => data.Length >= Magic.Length + NonceSize + TagSize && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static bool IsSealedFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[Magic.Length];
            return stream.Read(head, 0, head.Length) == head.Length && head.AsSpan().SequenceEqual(Magic);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static byte[] Seal(byte[] plain, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var output = new byte[Magic.Length + NonceSize + TagSize + plain.Length];
        Magic.CopyTo(output, 0);
        nonce.CopyTo(output, Magic.Length);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain,
            output.AsSpan(Magic.Length + NonceSize + TagSize),
            output.AsSpan(Magic.Length + NonceSize, TagSize));
        return output;
    }

    public static byte[] Open(byte[] data, byte[] key)
    {
        var nonce = data.AsSpan(Magic.Length, NonceSize);
        var tag = data.AsSpan(Magic.Length + NonceSize, TagSize);
        var cipher = data.AsSpan(Magic.Length + NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    // I hand back plain bytes whether the file on disk is sealed or not
    public static byte[] ReadBytes(string path)
    {
        var data = File.ReadAllBytes(path);
        if (!IsSealed(data)) return data;
        if (Key == null)
        {
            Log.Error("Crypto", $"Asked to read {Path.GetFileName(path)} but the diary key isn't loaded");
            throw new InvalidOperationException("The diary is locked, so this file can't be opened yet.");
        }
        try
        {
            return Open(data, Key);
        }
        catch (Exception ex)
        {
            Log.Error("Crypto", $"Couldn't unseal {Path.GetFileName(path)} ({data.Length:N0} bytes). It may be damaged or sealed with a different key", ex);
            throw;
        }
    }

    public static string ReadText(string path) => Encoding.UTF8.GetString(ReadBytes(path));

    // I seal what I'm about to write if my diary is encrypted
    public static byte[] Prepare(byte[] plain) => SealWrites && Key != null ? Seal(plain, Key) : plain;

    public static void Forget()
    {
        Log.Info("Crypto", "Forgetting the diary key");
        if (Key != null) CryptographicOperations.ZeroMemory(Key);
        Key = null;
        SealWrites = false;
    }
}

// I keep the diary key wrapped twice in Diary\keys.json: once by my password and once by my recovery file
public static class KeyStore
{
    private const int PasswordIterations = 300_000;
    private const int RecoveryIterations = 10_000;

    private sealed class Slot
    {
        public string Salt { get; set; } = "";
        public int Iterations { get; set; }
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Wrapped { get; set; } = "";
    }

    private sealed class KeyFile
    {
        public int Version { get; set; } = 1;
        public string Note { get; set; } = "This is the lock for a Kaydence diary. Without it and the password or recovery file, the diary can't be opened.";
        public Slot? Password { get; set; }
        public Slot? Recovery { get; set; }
    }

    public static string PathFor(string root) => Path.Combine(root, "keys.json");

    public static bool Exists(string root) => File.Exists(PathFor(root));

    public static bool HasRecovery(string root) => Load(root)?.Recovery != null;

    public static byte[] CreateNew(string root, string password)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var file = new KeyFile { Password = Wrap(key, password, PasswordIterations, "password") };
        Save(root, file);
        Log.Info("Crypto", "Made a new diary key and locked it with my password");
        return key;
    }

    public static byte[]? UnlockWithPassword(string root, string password)
    {
        var file = Load(root);
        if (file?.Password == null)
        {
            Log.Warn("Crypto", "There's no password lock in keys.json");
            return null;
        }
        var key = Unwrap(file.Password, password, "password");
        Log.Info("Crypto", key != null ? "Unlocked with my password" : "Password didn't open the diary");
        return key;
    }

    public static byte[]? UnlockWithRecovery(string root, string recoveryKey)
    {
        var file = Load(root);
        if (file?.Recovery == null)
        {
            Log.Warn("Crypto", "There's no recovery lock in keys.json");
            return null;
        }
        var key = Unwrap(file.Recovery, recoveryKey, "recovery");
        Log.Info("Crypto", key != null ? "Unlocked with my recovery file" : "Recovery file didn't open the diary");
        return key;
    }

    public static void SetPassword(string root, byte[] key, string password)
    {
        var file = Load(root) ?? new KeyFile();
        file.Password = Wrap(key, password, PasswordIterations, "password");
        Save(root, file);
        Log.Info("Crypto", "Locked the diary key with my new password");
    }

    public static void SetRecovery(string root, byte[] key, string recoveryKey)
    {
        var file = Load(root) ?? new KeyFile();
        file.Recovery = Wrap(key, recoveryKey, RecoveryIterations, "recovery");
        Save(root, file);
        Log.Info("Crypto", "Locked the diary key with my new recovery file");
    }

    public static void Delete(string root)
    {
        if (!Exists(root)) return;
        File.Delete(PathFor(root));
        Log.Info("Crypto", "Deleted keys.json, the diary is no longer encrypted");
    }

    private static Slot Wrap(byte[] key, string secret, int iterations, string label)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var kek = Rfc2898DeriveBytes.Pbkdf2(secret, salt, iterations, HashAlgorithmName.SHA256, 32);
        var wrapped = new byte[key.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(kek, 16)) aes.Encrypt(nonce, key, wrapped, tag, Encoding.UTF8.GetBytes(label));
        CryptographicOperations.ZeroMemory(kek);
        return new Slot
        {
            Salt = Convert.ToBase64String(salt),
            Iterations = iterations,
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Wrapped = Convert.ToBase64String(wrapped)
        };
    }

    // I get the diary key back out, or nothing if the password or recovery key is wrong
    private static byte[]? Unwrap(Slot slot, string secret, string label)
    {
        try
        {
            var kek = Rfc2898DeriveBytes.Pbkdf2(secret, Convert.FromBase64String(slot.Salt), slot.Iterations, HashAlgorithmName.SHA256, 32);
            var wrapped = Convert.FromBase64String(slot.Wrapped);
            var key = new byte[wrapped.Length];
            using (var aes = new AesGcm(kek, 16))
                aes.Decrypt(Convert.FromBase64String(slot.Nonce), wrapped, Convert.FromBase64String(slot.Tag), key, Encoding.UTF8.GetBytes(label));
            CryptographicOperations.ZeroMemory(kek);
            return key;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static KeyFile? Load(string root)
    {
        try
        {
            return Exists(root) ? JsonSerializer.Deserialize<KeyFile>(File.ReadAllText(PathFor(root))) : null;
        }
        catch (Exception ex)
        {
            Log.Error("Crypto", "keys.json couldn't be read", ex);
            return null;
        }
    }

    private static void Save(string root, KeyFile file)
    {
        Directory.CreateDirectory(root);
        var path = PathFor(root);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}

// I let Windows guard a copy of the diary key for my account, so "only ask after a while" still works when encrypted
public static class DeviceKey
{
    private const int UiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static string? Protect(byte[] key) => Run(key, true) is { } bytes ? Convert.ToBase64String(bytes) : null;

    public static byte[]? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            return Run(Convert.FromBase64String(stored), false);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[]? Run(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        var input = new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
        var output = new DataBlob();
        try
        {
            var ok = protect
                ? CryptProtectData(ref input, "Kaydence", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok || output.Data == IntPtr.Zero)
            {
                Log.Warn("Crypto", $"Windows couldn't {(protect ? "protect" : "unprotect")} the diary key, error {Marshal.GetLastWin32Error()}");
                return null;
            }
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            handle.Free();
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
