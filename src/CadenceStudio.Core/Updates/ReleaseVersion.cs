using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CadenceStudio.Core.Updates;

public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^(?<core>[0-9]+(?:\.[0-9]+)+)(?:-dev\.(?<dev>[0-9]+(?:\.[0-9]+)*))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly BigInteger[] _core;
    private readonly BigInteger[] _development;

    private ReleaseVersion(string original, BigInteger[] core, BigInteger[] development, bool isDevelopment)
    {
        Original = original;
        _core = core;
        _development = development;
        IsDevelopment = isDevelopment;
    }

    public string Original { get; }
    public bool IsDevelopment { get; }

    public static bool TryParse(string? value, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        if (!TryParseComponents(match.Groups["core"].Value, out var core))
        {
            return false;
        }

        var isDevelopment = match.Groups["dev"].Success;
        var development = Array.Empty<BigInteger>();
        if (isDevelopment &&
            !TryParseComponents(match.Groups["dev"].Value, out development))
        {
            return false;
        }

        version = new ReleaseVersion(value, core, development, isDevelopment);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var coreComparison = CompareComponents(_core, other._core);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (IsDevelopment != other.IsDevelopment)
        {
            return IsDevelopment ? -1 : 1;
        }

        return IsDevelopment
            ? CompareComponents(_development, other._development)
            : 0;
    }

    public override string ToString() => Original;

    private static bool TryParseComponents(string value, out BigInteger[] components)
    {
        var parts = value.Split('.');
        components = new BigInteger[parts.Length];

        for (var index = 0; index < parts.Length; index++)
        {
            if (!BigInteger.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out components[index]) ||
                components[index] < BigInteger.Zero)
            {
                components = [];
                return false;
            }
        }

        return true;
    }

    private static int CompareComponents(IReadOnlyList<BigInteger> left, IReadOnlyList<BigInteger> right)
    {
        var length = Math.Max(left.Count, right.Count);
        for (var index = 0; index < length; index++)
        {
            var leftValue = index < left.Count ? left[index] : BigInteger.Zero;
            var rightValue = index < right.Count ? right[index] : BigInteger.Zero;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }
}
