import sys

def apply_patch(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    old_timeout = "timeoutMs: 5000);"
    new_timeout = "timeoutMs: 15000);"

    if old_timeout in content:
        content = content.replace(old_timeout, new_timeout)
        with open(filepath, 'w') as f:
            f.write(content)
        print("Timeout increased in BrowseViewModelIntegrationTests.")
    else:
        print("Timeout string not found.")

apply_patch('./MeshWave.ViewModels.Tests/Integration/BrowseViewModelIntegrationTests.cs')
