from playwright.sync_api import sync_playwright
import time

def run():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        context = browser.new_context(bypass_csp=True)
        page = context.new_page()
        
        # Listen for console messages
        def handle_console(msg):
            if msg.type == 'error':
                print(f"JS ERROR: {msg.text}")
            else:
                pass # ignore normal logs to find the error
                
        def handle_error(err):
            print(f"PAGE EXCEPTION: {err.error}")

        page.on("console", handle_console)
        page.on("pageerror", handle_error)
        
        print("Navigating to http://vm202.panther-carat.ts.net:5000/ ...")
        response = page.goto('http://vm202.panther-carat.ts.net:5000/')
        print(f"HTTP Status: {response.status}")
        
        time.sleep(5)
        
        print("Trying to navigate to /book/23004")
        page.goto('http://vm202.panther-carat.ts.net:5000/book/23004')
        time.sleep(5)
        
        browser.close()

if __name__ == '__main__':
    run()
