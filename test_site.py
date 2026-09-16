from playwright.sync_api import sync_playwright
import time

def run():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        page = browser.new_page()
        
        errors = []
        page.on("console", lambda msg: errors.append(f"CONSOLE {msg.type}: {msg.text}") if msg.type == "error" else None)
        page.on("pageerror", lambda err: errors.append(f"JS ERROR: {err}"))
        page.on("response", lambda resp: errors.append(f"NETWORK ERROR: {resp.status} {resp.url}") if resp.status >= 400 else None)
        
        print("Navigating to URL...")
        page.goto("http://vm202.panther-carat.ts.net:5000/")
        time.sleep(3)
        
        page.screenshot(path="main.png")
        print("Screenshot main.png saved.")
        
        print("Clicking Settings...")
        try:
            page.click("a[href='settings']")
            time.sleep(2)
            page.screenshot(path="settings.png")
            print("Screenshot settings.png saved.")
        except Exception as e:
            print("Could not click settings:", e)
            
        print("\n--- ERRORS FOUND ---")
        for err in errors:
            print(err)
        if not errors:
            print("No console or network errors detected!")
            
        browser.close()

run()
