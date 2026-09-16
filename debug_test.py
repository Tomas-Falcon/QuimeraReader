import asyncio
from playwright.async_api import async_playwright

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page(viewport={"width": 1280, "height": 720})
        await page.goto("http://vm202.panther-carat.ts.net:5000/book/9830", wait_until="networkidle")
        await page.wait_for_timeout(2000)
        await page.screenshot(path="debug_book_details.png")
        await browser.close()

asyncio.run(main())
