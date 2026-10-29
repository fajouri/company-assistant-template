"""Dependency-free API regression coverage. Run after the Release .NET build."""
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time
import unittest
from urllib.request import urlopen
from urllib.error import URLError

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / "src/CompanyAssistant.Web/bin/Release/net10.0/CompanyAssistant.Web.dll"

class BrandingLogoTests(unittest.TestCase):
    def check_logo(self, configured, expected, language="es"):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "wwwroot/branding").mkdir(parents=True)
            logo = ROOT / "frontend/public/logo.svg"
            shutil.copyfile(logo, root / "wwwroot/logo.svg")
            shutil.copyfile(logo, root / "wwwroot/branding/company-logo.svg")
            with socket.socket() as listener:
                listener.bind(("127.0.0.1", 0))
                port = listener.getsockname()[1]
            address = f"http://127.0.0.1:{port}"
            env = {key: value for key, value in os.environ.items()
                   if not key.lower().startswith(("branding__", "aspnetcore_", "dotnet_"))}
            env.update(ASPNETCORE_URLS=address, ASPNETCORE_CONTENTROOT=directory,
                       Branding__DefaultLanguage=language)
            if configured is not None:
                (root / "appsettings.json").write_text(
                    json.dumps({"Branding": {"LogoUrl": configured}}), encoding="utf-8")
            with tempfile.TemporaryFile() as logs:
                process = subprocess.Popen(["dotnet", str(DLL)], cwd=directory,
                                           env=env, stdout=logs, stderr=logs)
                try:
                    deadline = time.monotonic() + 15
                    while True:
                        try:
                            with urlopen(address + "/api/branding", timeout=1) as response:
                                branding = json.load(response)
                            break
                        except (URLError, TimeoutError):
                            if process.poll() is not None or time.monotonic() > deadline:
                                logs.seek(0)
                                self.fail(logs.read().decode())
                            time.sleep(0.1)
                    self.assertEqual(branding["logoUrl"], expected)
                    self.assertEqual(branding["defaultLanguage"], language)
                    self.assertEqual(branding["companyName"], "NovaWorks")
                    with urlopen(address + branding["logoUrl"], timeout=2) as response:
                        self.assertEqual(response.status, 200)
                        self.assertIn(b"<svg", response.read())
                finally:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()

    def test_default_and_custom_assets_in_both_languages(self):
        for language in ("es", "en"):
            for value in ("/logo.svg", "/branding/company-logo.svg"):
                with self.subTest(value=value, language=language):
                    self.check_logo(value, value, language)

    def test_missing_empty_and_unsafe_values_fall_back(self):
        values = [
            None, "", " ", " /logo.svg", "/logo.svg ",
            "https://example.com/logo.svg", "http://example.com/logo.svg",
            "//example.com/logo.svg", "///example.com/logo.svg",
            "data:image/svg+xml,test", "javascript:alert(1)",
            "logo.svg", "/", "/branding/", "/branding//logo.svg",
            "/../logo.svg", "/branding/../logo.svg", "/./logo.svg",
            "/branding/.../logo.svg", "/branding./logo.svg",
            "/%2e%2e/logo.svg", "/%252e%252e/logo.svg",
            "/%2f%2fexample.com/logo.svg", "/branding\\logo.svg",
            "/logo.svg?url=https://example.com", "/logo.svg#fragment",
            "/logo.svg\n", "/logo.svg\x00", "/logo space.svg",
            "/logo.html", "/logo", "/" + "a" * 1025 + ".svg",
        ]
        for value in values:
            with self.subTest(value=value):
                self.check_logo(value, "/logo.svg")

if __name__ == "__main__":
    unittest.main()
