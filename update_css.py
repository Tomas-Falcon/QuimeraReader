import re

css_path = 'QuimeraReader.Clients/QuimeraReader.Web/wwwroot/css/app.css'
with open(css_path, 'r', encoding='utf-8') as f:
    css = f.read()

colors = {
    '#1e1e1e': 'var(--bg-card)',
    '#18181b': 'var(--bg-card)',
    '#252525': 'var(--bg-input)',
    '#27272a': 'var(--border-color)',
    '#333': 'var(--border-color)',
    '#444': 'var(--border-color)',
    '#eee': 'var(--text-main)',
    '#fff': 'var(--text-main)',
    'white': 'var(--text-inverse)',
    '#000': 'var(--text-inverse)',
    '#09090b': 'var(--bg-main)',
    '#1c1c1e': 'var(--bg-hover)',
    '#2c2c2e': 'var(--bg-active)',
    '#ddd': 'var(--text-muted)',
    '#999': 'var(--text-muted)',
    '#a1a1aa': 'var(--text-muted)',
    '#71717a': 'var(--text-muted)',
    '#e4e4e7': 'var(--text-main)',
    '#e0e0e0': 'var(--text-main)',
    '#007bff': 'var(--primary-color)',
    '#3b82f6': 'var(--primary-color)',
    '#2563eb': 'var(--primary-hover)',
    '#0056b3': 'var(--primary-hover)',
    '#28a745': 'var(--success-color)',
    '#10b981': 'var(--success-color)',
    '#218838': 'var(--success-hover)',
    '#059669': 'var(--success-hover)'
}

for hex_val, var_name in colors.items():
    css = re.sub(rf'(?i)(?<=[:\s]){hex_val}(?=[;\s}}])', var_name, css)

vars_css = """
:root {
    --bg-main: #f4f4f5;
    --bg-card: #ffffff;
    --bg-input: #ffffff;
    --bg-hover: #e4e4e7;
    --bg-active: #d4d4d8;
    --border-color: #e4e4e7;
    --text-main: #18181b;
    --text-inverse: #ffffff;
    --text-muted: #71717a;
    --primary-color: #3b82f6;
    --primary-hover: #2563eb;
    --success-color: #10b981;
    --success-hover: #059669;
}

[data-theme="dark"] {
    --bg-main: #09090b;
    --bg-card: #18181b;
    --bg-input: #09090b;
    --bg-hover: #27272a;
    --bg-active: #3f3f46;
    --border-color: #27272a;
    --text-main: #ffffff;
    --text-inverse: #000000;
    --text-muted: #a1a1aa;
    --primary-color: #3b82f6;
    --primary-hover: #2563eb;
    --success-color: #10b981;
    --success-hover: #059669;
}

body {
    background-color: var(--bg-main);
    color: var(--text-main);
    transition: background-color 0.3s, color 0.3s;
}
"""

with open(css_path, 'w', encoding='utf-8') as f:
    f.write(vars_css + css)
