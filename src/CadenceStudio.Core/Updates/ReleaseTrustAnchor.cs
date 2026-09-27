namespace CadenceStudio.Core.Updates;

// Reviewed local trust configuration. Private signing material must remain outside this repository.
// Existing cadence-release-2026-01 remains active; vNext does not require rotation merely to adopt
// the preferred P-384 / SHA-384 profile for future identities.
internal static class ReleaseTrustAnchor
{
    internal static IReadOnlyList<ReleaseTrustKey> Keys { get; } =
    [
        new ReleaseTrustKey(
            "cadence-release-2026-01",
            ReleaseTrustRole.Release,
            ReleaseKeyLifecycle.Active,
            "ecdsa-sha256",
            256,
            "a74a231a353a9ab28d9368b931620e98d1e5cb52f7416f56b1d8dc4c81c405df",
            """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKzja2RJnemUzPZYXAik8uvvTIujy
V8wIDdEflhsiuNe9Xsjn6g6olcfqC/DJx7IaAw0zeCNUZDOLt5RWTh3qDg==
-----END PUBLIC KEY-----
""")
    ];
}
