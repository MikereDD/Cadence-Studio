namespace CadenceStudio.Core.Updates;

internal static class ReleaseTrustAnchor
{
    private const string TestPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEavsFOOpCTpCrLfbSKfrTWmZ6l2/W
k+NWP7SvtoihuCA3PXsQoHLEdui+l+V8FhURsATb1gSxqBR79V7pmRM7Bw==
-----END PUBLIC KEY-----
""";

    internal static IReadOnlyList<ReleaseTrustKey> Keys { get; } =
    [
        new ReleaseTrustKey(
            "cadence-test-only",
            ReleaseTrustRole.Release,
            ReleaseKeyLifecycle.Active,
            "ecdsa-sha256",
            256,
            "8ad0ccf7274bb153f1e8c31c986b59d44c4c690a036168534f9337f0362894fb",
            TestPublicKeyPem),
        new ReleaseTrustKey(
            "cadence-test-pending",
            ReleaseTrustRole.Release,
            ReleaseKeyLifecycle.Pending,
            "ecdsa-sha256",
            256,
            "8ad0ccf7274bb153f1e8c31c986b59d44c4c690a036168534f9337f0362894fb",
            TestPublicKeyPem),
        new ReleaseTrustKey(
            "cadence-test-retired",
            ReleaseTrustRole.Release,
            ReleaseKeyLifecycle.Retired,
            "ecdsa-sha256",
            256,
            "8ad0ccf7274bb153f1e8c31c986b59d44c4c690a036168534f9337f0362894fb",
            TestPublicKeyPem),
        new ReleaseTrustKey(
            "cadence-test-revoked",
            ReleaseTrustRole.Release,
            ReleaseKeyLifecycle.Revoked,
            "ecdsa-sha256",
            256,
            "8ad0ccf7274bb153f1e8c31c986b59d44c4c690a036168534f9337f0362894fb",
            TestPublicKeyPem),
        new ReleaseTrustKey(
            "cadence-test-recovery",
            ReleaseTrustRole.Recovery,
            ReleaseKeyLifecycle.Active,
            "ecdsa-sha256",
            256,
            "8ad0ccf7274bb153f1e8c31c986b59d44c4c690a036168534f9337f0362894fb",
            TestPublicKeyPem)
    ];
}
