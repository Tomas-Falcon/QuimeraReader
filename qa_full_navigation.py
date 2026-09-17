# -*- coding: utf-8 -*-
from playwright.sync_api import sync_playwright
import time
import json

def run():
    print("Iniciando QA Completo de E2E...")
    logs = []
    
    def on_console(msg):
        if msg.type in ['error', 'warning']:
            logs.append(f"[{msg.type.upper()}] {msg.text}")
            
    def on_page_error(exc):
        logs.append(f"[EXCEPTION] {exc}")

    with sync_playwright() as p:
        browser = p.chromium.launch(headless=False, slow_mo=400)
        context = browser.new_context()
        page = context.new_page()
        
        # Suscribirse a eventos de consola
        page.on("console", on_console)
        page.on("pageerror", on_page_error)
        
        print("-> Navegando a Inicio (Biblioteca)...")
        page.goto('http://vm202.panther-carat.ts.net:5000/')
        time.sleep(3)
        
        print("-> Navegando a Autores...")
        page.get_by_text("Autores").first.click()
        time.sleep(3)
        
        print("-> Filtrando por un Autor...")
        # Hacer click en la primera tarjeta de autor (o enlace que contiene autor)
        author_cards = page.query_selector_all(".card-body")
        if len(author_cards) > 0:
            author_cards[0].click()
            time.sleep(3)
            
        print("-> Navegando a Géneros...")
        page.goto('http://vm202.panther-carat.ts.net:5000/categories')
        time.sleep(3)
        
        print("-> Filtrando por un Género...")
        cat_cards = page.query_selector_all(".card-body")
        if len(cat_cards) > 0:
            cat_cards[0].click()
            time.sleep(3)
            
        print("-> Navegando a Ajustes...")
        page.goto('http://vm202.panther-carat.ts.net:5000/settings')
        time.sleep(3)
        
        print("-> Regresando a Biblioteca y abriendo Detalles de un Libro...")
        page.goto('http://vm202.panther-carat.ts.net:5000/')
        time.sleep(2)
        book_cards = page.query_selector_all(".book-card")
        if len(book_cards) > 0:
            book_cards[-1].click() # Click en un libro para ver detalles
            time.sleep(3)
            
        print("-> QA finalizado. Cerrando el navegador...")
        time.sleep(2)
        browser.close()
        
        print("\n=== REPORTE DE CONSOLA (ERRORES Y ADVERTENCIAS) ===")
        if not logs:
            print("¡No se detectaron errores ni advertencias en la consola! Todo limpio.")
        else:
            for log in logs:
                print(log)

if __name__ == '__main__':
    run()
