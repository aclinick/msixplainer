using System.Security.Cryptography;
using System.Text;

namespace MSIXplainer.Services;

/// <summary>
/// Computes the Package Family Name (PFN) and Package Full Name from the
/// <c>&lt;Identity&gt;</c> element of an AppxManifest.
///
/// These are the canonical strings IT pros need for AppLocker rules,
/// Intune detection scripts, WDAC policies, and PowerShell
/// <c>Get-AppxPackage</c> queries — formats documented by Microsoft at
/// <see href="https://learn.microsoft.com/uwp/schemas/appxpackage/uapmanifestschema/element-identity"/>.
///
/// Format reference:
/// <list type="bullet">
///   <item>Package Family Name = <c>Name_PublisherHash</c></item>
///   <item>Package Full Name   = <c>Name_Version_Architecture_ResourceId_PublisherHash</c></item>
/// </list>
/// </summary>
public static class PackageIdentityCalculator
{
    // Crockford-style base32 alphabet used by MSIX publisher hashes.
    // Note the missing 'i', 'l', 'o', 'u' (visual collision avoidance).
    private const string Base32Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>
    /// Computes the 13-character publisher hash for an MSIX package given
    /// the <c>Publisher</c> attribute value (the X.509 Subject DN string,
    /// e.g. <c>"CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"</c>).
    ///
    /// Algorithm (per Microsoft):
    /// <list type="number">
    ///   <item>Encode the publisher string as UTF-16 LE bytes.</item>
    ///   <item>SHA-256 hash those bytes.</item>
    ///   <item>Take the first 8 bytes (64 bits) of the hash.</item>
    ///   <item>Encode those 64 bits as 13 characters using the Crockford-variant base32 alphabet
    ///   (the 13th character represents 4 bits, zero-padded on the right).</item>
    /// </list>
    /// </summary>
    public static string ComputePublisherHash(string publisher)
    {
        if (string.IsNullOrEmpty(publisher))
            return string.Empty;

        // .NET's Encoding.Unicode is UTF-16 LE — exactly what MSIX expects.
        var utf16Bytes = Encoding.Unicode.GetBytes(publisher);
        var hash = SHA256.HashData(utf16Bytes);
        var truncated = hash.AsSpan(0, 8);

        // 64 bits → 13 base32 chars (12 full 5-bit groups + 1 four-bit group padded right with 0).
        var result = new char[13];
        int bitBuffer = 0;
        int bitsInBuffer = 0;
        int outputIdx = 0;

        for (int i = 0; i < 8; i++)
        {
            bitBuffer = (bitBuffer << 8) | truncated[i];
            bitsInBuffer += 8;
            while (bitsInBuffer >= 5)
            {
                int idx = (bitBuffer >> (bitsInBuffer - 5)) & 0x1F;
                result[outputIdx++] = Base32Alphabet[idx];
                bitsInBuffer -= 5;
            }
        }
        // 4 leftover bits → left-shift to occupy top 5 bits (pad right with 0).
        if (bitsInBuffer > 0)
        {
            int idx = (bitBuffer << (5 - bitsInBuffer)) & 0x1F;
            result[outputIdx] = Base32Alphabet[idx];
        }

        return new string(result);
    }

    /// <summary>
    /// Builds the Package Family Name: <c>Name_PublisherHash</c>.
    /// This is the string Windows uses to identify the package family across
    /// versions and architectures — what <c>Get-AppxPackage</c> returns and
    /// what AppLocker / WDAC policies reference.
    /// </summary>
    public static string ComputePackageFamilyName(string name, string publisher)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(publisher))
            return string.Empty;
        return $"{name}_{ComputePublisherHash(publisher)}";
    }

    /// <summary>
    /// Builds the Package Full Name: <c>Name_Version_Architecture_ResourceId_PublisherHash</c>.
    /// Identifies a specific installed package instance. Used by
    /// <c>Add-AppxPackage</c>, <c>Remove-AppxPackage</c>, and the WMI
    /// <c>Win32_InstalledStoreProgram</c> class.
    /// </summary>
    /// <param name="name">Identity@Name attribute value.</param>
    /// <param name="version">Identity@Version attribute value.</param>
    /// <param name="architecture">Identity@ProcessorArchitecture (e.g. <c>x64</c>, <c>arm64</c>, <c>neutral</c>). Lowercased per Microsoft convention.</param>
    /// <param name="resourceId">Identity@ResourceId — usually empty for primary packages, non-empty for resource (language) packages.</param>
    /// <param name="publisher">Identity@Publisher attribute value (X.509 subject DN).</param>
    public static string ComputePackageFullName(
        string name,
        string version,
        string architecture,
        string resourceId,
        string publisher)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(publisher))
            return string.Empty;
        var arch = string.IsNullOrEmpty(architecture) ? "neutral" : architecture.ToLowerInvariant();
        var hash = ComputePublisherHash(publisher);
        return $"{name}_{version}_{arch}_{resourceId}_{hash}";
    }
}
