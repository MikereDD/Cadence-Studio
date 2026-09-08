namespace CadenceStudio.Core.Updates;

// Reviewed local trust anchor. Private signing material must remain outside this repository.
internal static class ReleaseTrustAnchor
{
    internal const string KeyId = "cadence-release-2026-01";
    internal const string Algorithm = "ecdsa-sha256";
    internal const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKzja2RJnemUzPZYXAik8uvvTIujy
V8wIDdEflhsiuNe9Xsjn6g6olcfqC/DJx7IaAw0zeCNUZDOLt5RWTh3qDg==
-----END PUBLIC KEY-----
""";
}