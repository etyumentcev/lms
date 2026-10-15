namespace Lms.Infrastructure.Tests;

public sealed class ContentImportRuntimeOptionsTests
{
    [Fact]
    public void Local_synthetic_socket_profile_accepts_only_absolute_socket_and_storage_paths()
    {
        new ContentImportRuntimeOptions
        {
            StorageRoot = "/var/lib/lms/content-import",
            LocalSyntheticSocketPath = "/run/lms-import-proxy/import.sock"
        }.Validate();

        Assert.Throws<InvalidOperationException>(() => new ContentImportRuntimeOptions
        {
            StorageRoot = "/var/lib/lms/content-import",
            LocalSyntheticSocketPath = "relative.sock"
        }.Validate());
    }

    [Fact]
    public void Default_profile_still_requires_the_accepted_local_runtime_paths()
    {
        Assert.Throws<InvalidOperationException>(() => new ContentImportRuntimeOptions
        {
            StorageRoot = "/var/lib/lms/content-import"
        }.Validate());
    }
}
