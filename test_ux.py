import asyncio
from playwright.async_api import async_playwright
import time

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page(viewport={"width": 1280, "height": 720})
        
        await page.goto("http://vm202.panther-carat.ts.net:5000/", wait_until="networkidle")
        await page.wait_for_timeout(2000) # wait for Blazor WebAssembly to render
        
        await page.screenshot(path="library_ux.png")
        
        # Test clicking settings
        await page.click("a[href='settings']")
        await page.wait_for_timeout(2000)
        await page.screenshot(path="settings_ux.png")
        
        print("Screenshots taken.")
        await browser.close()

asyncio.run(main())
