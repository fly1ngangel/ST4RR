from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from urllib.request import urlopen
from zipfile import ZipFile
import shutil

TOOLS = Path(__file__).resolve().parent
PACKAGES = {
    "microsoft.net.compilers.toolset": "4.8.0",
    "microsoft.netframework.referenceassemblies.net48": "1.0.3",
    "newtonsoft.json": "13.0.3",
    "litedb": "5.0.17",
    "htmlagilitypack": "1.11.72",
}

def download_package(item):
    name, version = item
    destination = TOOLS / name
    marker = destination / ".package-version"
    if marker.exists() and marker.read_text().strip() == version:
        return f"{name} {version}: ready"
    archive = TOOLS / f"{name}.nupkg"
    url = f"https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg"
    with urlopen(url, timeout=60) as response, archive.open("wb") as output:
        shutil.copyfileobj(response, output)
    with ZipFile(archive) as package:
        package.extractall(destination)
    marker.write_text(version, encoding="utf-8")
    return f"{name} {version}: downloaded"

if __name__ == "__main__":
    with ThreadPoolExecutor(max_workers=4) as executor:
        for result in executor.map(download_package, PACKAGES.items()):
            print(result, flush=True)
