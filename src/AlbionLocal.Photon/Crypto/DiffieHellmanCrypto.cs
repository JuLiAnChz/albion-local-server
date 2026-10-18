using System.Numerics;
using System.Security.Cryptography;

namespace AlbionLocal.Photon.Crypto;

/// <summary>
/// Photon payload encryption, matched against the client's DiffieHellmanCryptoProvider (g8q):
/// DH over RFC 2409 group 1 (768-bit OAKLEY prime), generator 22, 160-bit secret.
/// Public keys travel as unsigned big-endian bytes. AES key = SHA-256 of the shared secret
/// (unsigned big-endian); AES-256-CBC, IV = 16 zero bytes, PKCS7 padding.
/// </summary>
public sealed class DiffieHellmanCrypto : IDisposable
{
    private static readonly BigInteger Prime = BigInteger.Parse(
        "00FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DD" +
        "EF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A63A3620FFFFFFFFFFFFFFFF",
        System.Globalization.NumberStyles.HexNumber);

    private static readonly BigInteger Generator = 22;

    private readonly BigInteger _secret;
    private Aes? _aes;

    public DiffieHellmanCrypto()
    {
        Span<byte> buf = stackalloc byte[20];
        do
        {
            RandomNumberGenerator.Fill(buf);
            _secret = new BigInteger(buf, isUnsigned: true);
        } while (_secret < 2 || _secret >= Prime - 1);
        PublicKey = BigInteger.ModPow(Generator, _secret, Prime).ToByteArray(isUnsigned: true, isBigEndian: true);
    }

    public byte[] PublicKey { get; }

    public bool IsReady => _aes is not null;

    public void DeriveSharedKey(byte[] otherPublicKey)
    {
        var other = new BigInteger(otherPublicKey, isUnsigned: true, isBigEndian: true);
        if (other <= 1 || other >= Prime - 1) throw new CryptographicException("invalid DH public key");
        var shared = BigInteger.ModPow(other, _secret, Prime).ToByteArray(isUnsigned: true, isBigEndian: true);
        _aes?.Dispose();
        _aes = Aes.Create();
        _aes.Key = SHA256.HashData(shared);
        _aes.IV = new byte[16];
        _aes.Mode = CipherMode.CBC;
        _aes.Padding = PaddingMode.PKCS7;
    }

    public byte[] Encrypt(ReadOnlySpan<byte> data) =>
        (_aes ?? throw new InvalidOperationException("keys not exchanged")).EncryptCbc(data, _aes.IV, PaddingMode.PKCS7);

    public byte[] Decrypt(ReadOnlySpan<byte> data) =>
        (_aes ?? throw new InvalidOperationException("keys not exchanged")).DecryptCbc(data, _aes.IV, PaddingMode.PKCS7);

    public void Dispose() => _aes?.Dispose();
}
