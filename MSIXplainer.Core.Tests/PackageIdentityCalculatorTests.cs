using MSIXplainer.Services;

namespace MSIXplainer.Core.Tests;

public class PackageIdentityCalculatorTests
{
    // The famous one — Microsoft Store Calculator. The hash "8wekyb3d8bbwe"
    // is publicly documented and shows up on every Windows install.
    [Fact]
    public void PublisherHash_MicrosoftCorporation_MatchesKnownValue()
    {
        const string publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
        Assert.Equal("8wekyb3d8bbwe", PackageIdentityCalculator.ComputePublisherHash(publisher));
    }

    // MSIXplainer's own published package — ground truth from Partner Center.
    [Fact]
    public void PublisherHash_MSIXplainerPublisher_MatchesKnownValue()
    {
        const string publisher = "CN=46604BD4-AFD9-4B23-8EB3-10EAF66872A5";
        Assert.Equal("nxa64pw99ve20", PackageIdentityCalculator.ComputePublisherHash(publisher));
    }

    [Fact]
    public void PublisherHash_AlwaysReturns13Characters()
    {
        var samples = new[]
        {
            "CN=A",
            "CN=Some Corp, O=Some Corp",
            "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
            "CN=46604BD4-AFD9-4B23-8EB3-10EAF66872A5"
        };

        foreach (var publisher in samples)
        {
            var hash = PackageIdentityCalculator.ComputePublisherHash(publisher);
            Assert.Equal(13, hash.Length);
        }
    }

    [Fact]
    public void PublisherHash_UsesCrockfordAlphabet_NoVowelCollisions()
    {
        // Alphabet must omit i, l, o, u (visual collision avoidance).
        var samples = new[]
        {
            "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US",
            "CN=46604BD4-AFD9-4B23-8EB3-10EAF66872A5",
            "CN=Test Publisher 1234"
        };

        foreach (var publisher in samples)
        {
            var hash = PackageIdentityCalculator.ComputePublisherHash(publisher);
            Assert.DoesNotContain('i', hash);
            Assert.DoesNotContain('l', hash);
            Assert.DoesNotContain('o', hash);
            Assert.DoesNotContain('u', hash);
        }
    }

    [Fact]
    public void PublisherHash_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal("", PackageIdentityCalculator.ComputePublisherHash(""));
    }

    [Fact]
    public void PackageFamilyName_MSIXplainer_MatchesPartnerCenterValue()
    {
        var pfn = PackageIdentityCalculator.ComputePackageFamilyName(
            "Clinick.msixplainer",
            "CN=46604BD4-AFD9-4B23-8EB3-10EAF66872A5");
        Assert.Equal("Clinick.msixplainer_nxa64pw99ve20", pfn);
    }

    [Fact]
    public void PackageFamilyName_Calculator_MatchesWellKnownValue()
    {
        var pfn = PackageIdentityCalculator.ComputePackageFamilyName(
            "Microsoft.WindowsCalculator",
            "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US");
        Assert.Equal("Microsoft.WindowsCalculator_8wekyb3d8bbwe", pfn);
    }

    [Fact]
    public void PackageFullName_BuildsExpectedFormat()
    {
        var fullName = PackageIdentityCalculator.ComputePackageFullName(
            name: "Clinick.msixplainer",
            version: "1.0.16.0",
            architecture: "x64",
            resourceId: "",
            publisher: "CN=46604BD4-AFD9-4B23-8EB3-10EAF66872A5");
        Assert.Equal("Clinick.msixplainer_1.0.16.0_x64__nxa64pw99ve20", fullName);
    }

    [Fact]
    public void PackageFullName_LowercasesArchitecture()
    {
        var fullName = PackageIdentityCalculator.ComputePackageFullName(
            name: "Test.App",
            version: "1.0.0.0",
            architecture: "X64",
            resourceId: "",
            publisher: "CN=46604BD4-AFD9-4B23-8EB3-10EAF66872A5");
        Assert.Contains("_x64_", fullName);
    }

    [Fact]
    public void PackageFullName_EmptyArchitectureBecomesNeutral()
    {
        var fullName = PackageIdentityCalculator.ComputePackageFullName(
            name: "Test.App",
            version: "1.0.0.0",
            architecture: "",
            resourceId: "",
            publisher: "CN=46604BD4-AFD9-4B23-8EB3-10EAF66872A5");
        Assert.Contains("_neutral_", fullName);
    }

    [Fact]
    public void PackageFamilyName_EmptyInputs_ReturnsEmpty()
    {
        Assert.Equal("", PackageIdentityCalculator.ComputePackageFamilyName("", "CN=Test"));
        Assert.Equal("", PackageIdentityCalculator.ComputePackageFamilyName("App.Name", ""));
    }
}
