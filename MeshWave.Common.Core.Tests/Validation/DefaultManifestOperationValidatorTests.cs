using MeshWave.Common.Core.Models;
using MeshWave.Common.Core.Validation;
using Xunit;

namespace MeshWave.Common.Core.Tests.Validation;

public class DefaultManifestOperationValidatorTests
{
    private readonly DefaultManifestOperationValidator _validator = new();

    [Fact]
    public void IsValid_ValidOperation_ReturnsTrue()
    {
        var op = new ManifestOperation
        {
            OperationId = "valid-id",
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = "valid-target",
            TargetType = "valid-type",
            ContentHash = "valid-hash",
            Metadata = new Dictionary<string, string> { { "key", "value" } }
        };

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.True(result);
        Assert.Null(reason);
    }

    [Fact]
    public void IsValid_OperationIdTooLong_ReturnsFalse()
    {
        var op = new ManifestOperation
        {
            OperationId = new string('a', SecurityLimits.MaxOperationIdLength + 1),
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = "valid-target",
            TargetType = "valid-type"
        };

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.False(result);
        Assert.Contains("OperationId length", reason);
    }

    [Fact]
    public void IsValid_TargetIdTooLong_ReturnsFalse()
    {
        var op = new ManifestOperation
        {
            OperationId = "valid-id",
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = new string('a', SecurityLimits.MaxTargetIdLength + 1),
            TargetType = "valid-type"
        };

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.False(result);
        Assert.Contains("TargetId length", reason);
    }

    [Fact]
    public void IsValid_TargetTypeTooLong_ReturnsFalse()
    {
        var op = new ManifestOperation
        {
            OperationId = "valid-id",
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = "valid-target",
            TargetType = new string('a', SecurityLimits.MaxTargetTypeLength + 1)
        };

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.False(result);
        Assert.Contains("TargetType length", reason);
    }

    [Fact]
    public void IsValid_ContentHashTooLong_ReturnsFalse()
    {
        var op = new ManifestOperation
        {
            OperationId = "valid-id",
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = "valid-target",
            TargetType = "valid-type",
            ContentHash = new string('a', SecurityLimits.MaxContentHashLength + 1)
        };

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.False(result);
        Assert.Contains("ContentHash length", reason);
    }

    [Fact]
    public void IsValid_TooManyMetadataEntries_ReturnsFalse()
    {
        var op = new ManifestOperation
        {
            OperationId = "valid-id",
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = "valid-target",
            TargetType = "valid-type",
            Metadata = new Dictionary<string, string>()
        };

        for (int i = 0; i < SecurityLimits.MaxMetadataEntries + 1; i++)
        {
            op.Metadata.Add($"key{i}", "value");
        }

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.False(result);
        Assert.Contains("Metadata count", reason);
    }

    [Fact]
    public void IsValid_MetadataKeyTooLong_ReturnsFalse()
    {
        var op = new ManifestOperation
        {
            OperationId = "valid-id",
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = "valid-target",
            TargetType = "valid-type",
            Metadata = new Dictionary<string, string>
            {
                { new string('a', SecurityLimits.MaxMetadataKeyLength + 1), "value" }
            }
        };

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.False(result);
        Assert.Contains("Metadata Key length", reason);
    }

    [Fact]
    public void IsValid_MetadataValueTooLong_ReturnsFalse()
    {
        var op = new ManifestOperation
        {
            OperationId = "valid-id",
            OperationType = ManifestOperationType.Create,
            Signature = "sig",
            TargetId = "valid-target",
            TargetType = "valid-type",
            Metadata = new Dictionary<string, string>
            {
                { "key", new string('a', SecurityLimits.MaxMetadataValueLength + 1) }
            }
        };

        var result = _validator.IsValid(op, "user-1", out var reason);

        Assert.False(result);
        Assert.Contains("Metadata Value length", reason);
    }
}
