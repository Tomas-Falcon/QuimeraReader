# -*- coding: utf-8 -*-
from playwright.sync_api import sync_playwright
import time

def run():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=False, slow_mo=500)
        page = browser.new_page()
        print("Navegando a QuimeraReader...")
        page.goto('http://vm202.panther-carat.ts.net:5000/')
        time.sleep(2)
        
        print("Revisando Autores...")
        page.get_by_text("Autores").first.click()
        time.sleep(3)
        
        print("Revisando Categorias...")
        page.get_by_text("G").first.click() # We can just click the 3rd nav item to avoid accents if it fails
        page.goto('http://vm202.panther-carat.ts.net:5000/categories')
        time.sleep(3)
        
        print("Revisando Detalles de un libro...")
        page.goto('http://vm202.panther-carat.ts.net:5000/')
        time.sleep(2)
        
        # Click the first book image
        book_cards = page.query_selector_all('img')
        if len(book_cards) > 2:
            book_cards[1].click()
            time.sleep(4)
        
        print("Prueba visual completada. Cerrando en 3 segundos...")
        time.sleep(3)
        browser.close()

if __name__ == '__main__':
    run()
