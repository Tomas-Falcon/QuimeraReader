import asyncio
from playwright.async_api import async_playwright

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        
        # Test Desktop
        page_desktop = await browser.new_page(viewport={"width": 1280, "height": 720})
        await page_desktop.goto("http://vm202.panther-carat.ts.net:5000/", wait_until="networkidle")
        await page_desktop.wait_for_timeout(2000)
        await page_desktop.screenshot(path="desktop_home.png")
        await page_desktop.close()
        print("Desktop screenshot taken.")
        
        # Test Mobile
        iphone_13 = p.devices['iPhone 13']
        context_mobile = await browser.new_context(**iphone_13)
        page_mobile = await context_mobile.new_page()
        await page_mobile.goto("http://vm202.panther-carat.ts.net:5000/", wait_until="networkidle")
        await page_mobile.wait_for_timeout(2000)
        await page_mobile.screenshot(path="mobile_home.png")
        
        # Click the hamburger menu
        await page_mobile.click(".navbar-toggler")
        await page_mobile.wait_for_timeout(1000)
        await page_mobile.screenshot(path="mobile_menu.png")
        
        await context_mobile.close()
        print("Mobile screenshots taken.")
        
        await browser.close()

asyncio.run(main())
