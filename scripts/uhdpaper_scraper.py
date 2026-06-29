#!/usr/bin/env python3
"""
uhdpaper_scraper.py — Download wallpapers from uhdpaper.com

Usage:
    python scripts/uhdpaper_scraper.py -r 4k -o ./wallpapers -p 2
    python scripts/uhdpaper_scraper.py -r phone-4k --max-pages 5 --workers 8
    python scripts/uhdpaper_scraper.py --dry-run
"""

from __future__ import annotations

import argparse
import logging
import os
import random
import re
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional
from urllib.parse import urljoin, urlparse

import requests
from bs4 import BeautifulSoup

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

BASE_URL = "https://www.uhdpaper.com/"
IMAGE_BASE = "https://img.uhdpaper.com/wallpaper/"
REQUEST_TIMEOUT = 30
MAX_RETRIES = 3
BACKOFF_FACTOR = 2.0

USER_AGENTS = [
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
    "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:127.0) Gecko/20100101 Firefox/127.0",
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15 "
    "(KHTML, like Gecko) Version/17.5 Safari/605.1.15",
]

# Suffix mapping: CLI alias -> URL segment
RESOLUTION_MAP: dict[str, str] = {
    "4k": "pc-4k",
    "2k": "pc-2k",
    "hd": "pc-hd",
    "phone-4k": "phone-4k",
    "phone-hd": "phone-hd",
}


# ---------------------------------------------------------------------------
# Models
# ---------------------------------------------------------------------------

@dataclass
class Wallpaper:
    title: str
    post_url: str
    slug: str
    resolutions: dict[str, str] = field(default_factory=dict)

    @property
    def safe_filename(self) -> str:
        return self.slug.strip("-")


# ---------------------------------------------------------------------------
# Client
# ---------------------------------------------------------------------------

class UHDPaperClient:
    """Low-level HTTP client with retry + backoff."""

    def __init__(self, user_agent: Optional[str] = None) -> None:
        self._session = requests.Session()
        self._session.headers.update({
            "User-Agent": user_agent or random.choice(USER_AGENTS),
            "Accept-Language": "en-US,en;q=0.9",
        })
        self._logger = logging.getLogger("UHDPaperClient")

    def get(self, url: str) -> requests.Response:
        last: Optional[Exception] = None
        for attempt in range(1, MAX_RETRIES + 1):
            try:
                resp = self._session.get(url, timeout=REQUEST_TIMEOUT)
                resp.raise_for_status()
                return resp
            except requests.RequestException as exc:
                last = exc
                self._logger.warning(
                    "Attempt %d/%d for %s failed: %s", attempt, MAX_RETRIES, url, exc
                )
                if attempt < MAX_RETRIES:
                    time.sleep(BACKOFF_FACTOR ** attempt)
        raise RuntimeError(f"Failed to fetch {url} after {MAX_RETRIES} attempts") from last


# ---------------------------------------------------------------------------
# Scraper
# ---------------------------------------------------------------------------

class Scraper:
    """Scrapes uhdpaper.com listing pages and individual post pages."""

    POST_PATTERN = re.compile(r"/202\d/.*\.html")

    def __init__(self, client: Optional[UHDPaperClient] = None, delay: float = 1.0) -> None:
        self._client = client or UHDPaperClient()
        self._delay = delay
        self._logger = logging.getLogger("Scraper")

    # ---- Public API -------------------------------------------------------

    def get_post_urls(self, max_pages: int = 1) -> list[str]:
        urls: list[str] = []
        next_url: Optional[str] = BASE_URL
        page = 0

        while next_url and page < max_pages:
            page += 1
            self._logger.info("Listing page %d: %s", page, next_url)
            page_urls = self._extract_post_urls(next_url)
            urls.extend(page_urls)
            next_url = self._next_page_url(next_url)
            if next_url:
                time.sleep(self._delay)

        return self._dedup(urls)

    def scrape_post(self, post_url: str) -> Optional[Wallpaper]:
        try:
            resp = self._client.get(post_url)
            soup = BeautifulSoup(resp.text, "html.parser")
            slug = self._slug_from_url(post_url)
            title = slug.replace("-", " ").strip().title()

            resolutions: dict[str, str] = {}
            for a in soup.find_all("a", href=re.compile(r"https://img\.uhdpaper\.com/wallpaper/.+\.jpg$")):
                href = a.get("href")
                if not href:
                    continue
                for alias, suffix in RESOLUTION_MAP.items():
                    if f"-{suffix}." in href and alias not in resolutions:
                        resolutions[alias] = href
                        break

            return Wallpaper(
                title=title, post_url=post_url, slug=slug, resolutions=resolutions,
            )
        except Exception as exc:
            self._logger.error("Failed to scrape %s: %s", post_url, exc)
            return None

    # ---- Internal helpers ------------------------------------------------

    def _extract_post_urls(self, url: str) -> list[str]:
        resp = self._client.get(url)
        soup = BeautifulSoup(resp.text, "html.parser")
        found: set[str] = set()
        for a in soup.find_all("a", href=self.POST_PATTERN):
            href = a.get("href")
            if href:
                clean = href.split("?")[0]
                found.add(urljoin(url, clean))
        self._logger.debug("  → %d post links found", len(found))
        return sorted(found)

    def _next_page_url(self, current_url: str) -> Optional[str]:
        resp = self._client.get(current_url)
        soup = BeautifulSoup(resp.text, "html.parser")
        for a in soup.find_all("a"):
            text = (a.get_text(strip=True) or "").lower()
            href = a.get("href")
            if href and "next" in text:
                return urljoin(current_url, href)
        return None

    @staticmethod
    def _slug_from_url(post_url: str) -> str:
        path = urlparse(post_url).path
        name = path.rstrip(".html").rstrip("/")
        return name.rsplit("/", 1)[-1] if "/" in name else name

    @staticmethod
    def _dedup(items: list[str]) -> list[str]:
        seen: set[str] = set()
        result: list[str] = []
        for item in items:
            if item not in seen:
                seen.add(item)
                result.append(item)
        return result


# ---------------------------------------------------------------------------
# Downloader
# ---------------------------------------------------------------------------

class Downloader:
    """Downloads wallpapers concurrently."""

    def __init__(self, output_dir: Path, workers: int = 4) -> None:
        self._output_dir = output_dir
        self._workers = workers
        self._session = requests.Session()
        self._session.headers["User-Agent"] = random.choice(USER_AGENTS)
        self._logger = logging.getLogger("Downloader")

    def download(self, wallpaper: Wallpaper, resolution: str) -> Optional[Path]:
        url = wallpaper.resolutions.get(resolution)
        if not url:
            return None

        dest = self._output_dir / f"{wallpaper.safe_filename}-{resolution}.jpg"
        if dest.exists():
            self._logger.info("  ✔ %s (exists)", dest.name)
            return dest

        self._logger.info("  ↓ %s", dest.name)
        try:
            resp = self._session.get(url, timeout=REQUEST_TIMEOUT, stream=True)
            resp.raise_for_status()
            dest.parent.mkdir(parents=True, exist_ok=True)
            with open(dest, "wb") as f:
                for chunk in resp.iter_content(chunk_size=8192):
                    f.write(chunk)
            return dest
        except requests.RequestException as exc:
            self._logger.error("  ✘ %s — %s", dest.name, exc)
            if dest.exists():
                dest.unlink()
            return None

    def download_all(self, wallpapers: list[Wallpaper], resolution: str) -> list[Path]:
        results: list[Path] = []
        with ThreadPoolExecutor(max_workers=self._workers) as pool:
            fut_map = {
                pool.submit(self.download, wp, resolution): wp
                for wp in wallpapers
                if resolution in wp.resolutions
            }
            for future in as_completed(fut_map):
                path = future.result()
                if path is not None:
                    results.append(path)
        return results


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="uhdpaper_scraper",
        description="Download wallpapers from uhdpaper.com",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Examples:\n"
            "  %(prog)s -r 4k -o ./wallpapers -p 2\n"
            "  %(prog)s -r phone-4k --max-pages 5 --workers 8\n"
            "  %(prog)s --dry-run\n"
        ),
    )
    parser.add_argument(
        "-r", "--resolution",
        default="4k",
        choices=list(RESOLUTION_MAP),
        help="Image resolution to download (default: 4k)",
    )
    parser.add_argument(
        "-o", "--output",
        type=Path,
        default=Path("uhdpaper_wallpapers"),
        help="Output directory (default: ./uhdpaper_wallpapers)",
    )
    parser.add_argument(
        "-p", "--max-pages",
        type=int,
        default=1,
        help="Number of listing pages to scrape (default: 1)",
    )
    parser.add_argument(
        "-w", "--workers",
        type=int,
        default=4,
        help="Concurrent download workers (default: 4)",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=1.0,
        help="Seconds between page requests (default: 1.0)",
    )
    parser.add_argument(
        "-n", "--dry-run",
        action="store_true",
        help="List wallpapers without downloading",
    )
    parser.add_argument(
        "-v", "--verbose",
        action="store_true",
        help="Enable debug logging",
    )
    return parser


def _setup_logging(verbose: bool) -> None:
    level = logging.DEBUG if verbose else logging.INFO
    logging.basicConfig(
        level=level,
        format="%(asctime)s [%(levelname)-7s] %(message)s",
        datefmt="%H:%M:%S",
    )


def main() -> None:
    parser = _build_parser()
    args = parser.parse_args()
    _setup_logging(args.verbose)
    log = logging.getLogger("main")

    # Scrape
    scraper = Scraper(delay=args.delay)
    log.info("Scraping up to %d page(s) from %s …", args.max_pages, BASE_URL)
    post_urls = scraper.get_post_urls(max_pages=args.max_pages)
    log.info("Found %d wallpaper posts", len(post_urls))

    wallpapers: list[Wallpaper] = []
    for i, url in enumerate(post_urls, 1):
        log.info("[%d/%d] %s", i, len(post_urls), url)
        wp = scraper.scrape_post(url)
        if wp is not None:
            wallpapers.append(wp)
        time.sleep(args.delay)

    # Summary
    available = sum(1 for wp in wallpapers if args.resolution in wp.resolutions)
    log.info(
        "Scraped %d wallpapers (%d with '%s' resolution)",
        len(wallpapers), available, args.resolution,
    )

    if args.dry_run:
        log.info("Dry-run — wallpapers found:")
        for wp in wallpapers:
            url = wp.resolutions.get(args.resolution, "N/A")
            log.info("  %s → %s", wp.title, url)
        return

    # Download
    out = args.output.resolve()
    downloader = Downloader(output_dir=out, workers=args.workers)
    paths = downloader.download_all(wallpapers, args.resolution)
    log.info("Done — %d wallpapers saved to %s", len(paths), out)


if __name__ == "__main__":
    main()
