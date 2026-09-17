# -*- coding: utf-8 -*-
from playwright.sync_api import sync_playwright
import time
import os

def run():
    with sync_playwright() as p:
        browser = p.chromium.launch(headless=False, slow_mo=500)
        page = browser.new_page()
        print("Navegando al servidor remoto...")
        page.goto('http://vm202.panther-carat.ts.net:5000/')
        time.sleep(3)
        
        # 1. Check if the UI has the latest changes by looking for the "Subir EPUB" button which might be present.
        print("Buscando el input de archivo...")
        
        # In Blazor, the InputFile component renders an <input type="file">
        # Let's attach our file to it.
        file_input = page.query_selector("input[type='file']")
        if not file_input:
            print("ERROR: No se encontro el input para subir EPUB. La UI quiza no esta actualizada o cargo mal.")
        else:
            print("Subiendo el EPUB por primera vez...")
            file_path = os.path.abspath('libro_test_duplicado.epub')
            file_input.set_input_files(file_path)
            time.sleep(5) # wait for upload
            
            print("Subiendo el EPUB por segunda vez...")
            file_input = page.query_selector("input[type='file']")
            file_input.set_input_files(file_path)
            time.sleep(5) # wait for second upload
            
        print("Revisando los libros cargados...")
        page.reload()
        time.sleep(3)
        
        titles = page.query_selector_all('h3.book-title')
        dup_count = 0
        for title in titles:
            if 'Libro de Prueba Duplicados' in title.inner_text():
                dup_count += 1
                
        print(f"Cantidad de veces que aparece 'Libro de Prueba Duplicados': {dup_count}")
        
        if dup_count == 0:
            print("No aparecio el libro, quiza no se subio bien (servidor en version vieja).")
        elif dup_count > 1:
            print("ERROR: ¡Se duplicó! El servidor aún tiene la versión anterior de la app.")
        else:
            print("EXITO: El libro aparece solo una vez, la prevención de duplicados funciona.")
            
        print("Revisando la interfaz de detalles...")
        # Click the book to see details
        for t in titles:
            if 'Libro de Prueba Duplicados' in t.inner_text():
                t.click()
                break
                
        time.sleep(3)
        
        # Check if the "Saga:" or badgets are present
        page_content = page.content()
        if 'Saga:' in page_content or 'Puntuaci' in page_content:
            print("UI NUEVA DETECTADA: Se ven los campos de Saga o Puntuación.")
        else:
            print("UI VIEJA DETECTADA: No se ven los nuevos campos en los detalles.")
            
        print("Finalizando prueba...")
        time.sleep(3)
        browser.close()

if __name__ == '__main__':
    run()
