import asyncio
from playwright.async_api import async_playwright

async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        page = await browser.new_page(viewport={"width": 1280, "height": 720})
        
        results = []
        
        try:
            # 1. Load Home
            results.append("1. Navigating to Home...")
            await page.goto("http://vm202.panther-carat.ts.net:5000/", wait_until="networkidle")
            await page.wait_for_timeout(2000)
            books_count = await page.locator('.book-card').count()
            results.append(f"   -> Home loaded. Found {books_count} books.")
            
            # 2. Test Search Bar
            results.append("2. Testing Search Bar...")
            await page.fill('input[placeholder="Buscar libros, autores..."]', 'an_impossible_search_query_123')
            await page.wait_for_timeout(1000)
            empty_state = await page.locator('.empty-state').count()
            results.append(f"   -> Empty state shown when no results: {'YES' if empty_state > 0 else 'NO'}")
            await page.fill('input[placeholder="Buscar libros, autores..."]', '')
            await page.wait_for_timeout(1000)
            
            # 3. Navigate to Book Details
            results.append("3. Testing Book Details navigation...")
            if books_count > 0:
                await page.locator('.book-card').first.click()
                await page.wait_for_timeout(2000)
                url = page.url
                results.append(f"   -> URL changed to: {url}")
                btn_count = await page.locator('button:has-text("Reproducir")').count()
                results.append(f"   -> Found 'Reproducir' button: {'YES' if btn_count > 0 else 'NO'}")
            else:
                results.append("   -> Skipped (no books found).")
                
            # 4. Navigate to Settings via Sidebar
            results.append("4. Testing Settings navigation...")
            await page.click('a[href="settings"]')
            await page.wait_for_timeout(2000)
            url = page.url
            results.append(f"   -> URL changed to: {url}")
            tabs_count = await page.locator('.nav-tabs .tab-item').count()
            results.append(f"   -> Found {tabs_count} settings tabs.")
            
            # 5. Interact with Settings (Switch to API Keys Tab)
            results.append("5. Testing Settings Tabs Interaction...")
            await page.locator('.nav-tabs .tab-item', has_text="Integraciones").click()
            await page.wait_for_timeout(500)
            has_api_input = await page.locator('input[placeholder="sk-..."]').count()
            results.append(f"   -> Rendered API Keys tab correctly: {'YES' if has_api_input > 0 else 'NO'}")
            
            # 6. Test Scanning Button
            results.append("6. Testing Scan Trigger...")
            await page.locator('.nav-tabs .tab-item', has_text="Tareas").click()
            await page.wait_for_timeout(500)
            await page.locator('button:has-text("Iniciar Escaneo")').click()
            await page.wait_for_timeout(1000)
            alert_count = await page.locator('.toast-message.success').count()
            results.append(f"   -> Scan triggered and toast shown: {'YES' if alert_count > 0 else 'NO'}")
            
        except Exception as e:
            results.append(f"ERROR: {str(e)}")
            
        await browser.close()
        
        with open("e2e_results.txt", "w") as f:
            f.write("\n".join(results))
            
        print("E2E Test completed.")

asyncio.run(main())
