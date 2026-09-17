from playwright.sync_api import sync_playwright

def run():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=True)
        # Create a new context which is incognito by default
        context = browser.new_context()
        page = context.new_page()
        
        page.goto('http://vm202.panther-carat.ts.net:5000/authors')
        page.wait_for_selector('.card-title')
        
        # Check if the links are wrapped in <a href="/?q=...">
        links = page.query_selector_all('a[href^="/?q="]')
        print(f"Encontrados {len(links)} enlaces de autores con el nuevo formato.")
        
        browser.close()

if __name__ == '__main__':
    run()
