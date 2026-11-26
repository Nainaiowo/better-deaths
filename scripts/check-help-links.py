"""Check the in-game Help button destinations against the local or published site."""

import argparse
import re
import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import urljoin, urldefrag
from urllib.request import Request, urlopen


ROOT = Path(__file__).resolve().parents[1]


class PageSections(HTMLParser):
    def __init__(self):
        super().__init__()
        self.ids = set()

    def handle_starttag(self, tag, attrs):
        self.ids.update(value for key, value in attrs if key == "id")


def check(live):
    source = (ROOT / "BetterDeaths/Windows/RecapWindow.cs").read_text(encoding="utf-8-sig")
    base = re.search(r'const string HelpCenterUrl\s*=\s*"([^"]+)"', source)
    routes = set(re.findall(r'\$"\{HelpCenterUrl\}([^"]+)"', source))
    if not base or not routes:
        sys.exit("Could not read the Help button destinations from RecapWindow.cs.")

    # The default Help button opens the directory index; some pages have multiple targets.
    pages = {"": set()}
    for route in routes:
        page, section = urldefrag(route)
        sections = pages.setdefault(page, set())
        if section:
            sections.add(section)

    ok = True
    for page, sections in sorted(pages.items()):
        target = urljoin(base[1], page) if live else ROOT / "docs/help" / (page or "index.html")
        try:
            if live:
                request = Request(target, headers={"User-Agent": "BetterDeaths-Help-Check"})
                with urlopen(request, timeout=12) as response:
                    html = response.read().decode("utf-8-sig")
            else:
                html = target.read_text(encoding="utf-8-sig")
            document = PageSections()
            document.feed(html)
            document.close()
            missing = sections - document.ids
            if missing:
                raise ValueError("missing section(s): " + ", ".join("#" + s for s in sorted(missing)))
            print(f"OK: {target}")
        except (OSError, ValueError) as error:
            print(f"FAIL: {target}: {error}", file=sys.stderr)
            ok = False
    return ok


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--live", action="store_true", help="check GitHub Pages instead of docs/help")
    sys.exit(0 if check(parser.parse_args().live) else 1)
