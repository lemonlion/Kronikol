# python3 run-safari.py : the probe page in Safari through safaridriver (run `sudo safaridriver --enable` first), over
# http://127.0.0.1 and over file://. Each address is reported on its own; one that does not open says what Safari showed.
import http.server, os, threading, time
from selenium import webdriver
here = os.path.dirname(os.path.abspath(__file__))
handler = lambda *a, **k: http.server.SimpleHTTPRequestHandler(*a, directory=here, **k)
server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
urls = ['http://127.0.0.1:%d/probe.html' % server.server_address[1], 'file://' + os.path.join(here, 'probe.html')]
driver = webdriver.Safari()
try:
    for u in urls:
        scheme = u.split(':')[0] + '://'
        try:
            driver.get(u)
            for _ in range(100):
                if driver.title == 'done':
                    break
                time.sleep(0.2)
            print('safari', scheme, driver.find_element('id', 'out').text, flush=True)
        except Exception as e:
            print('safari', scheme, 'did not open: %s; title %r, url %r' % (type(e).__name__, driver.title, driver.current_url), flush=True)
finally:
    driver.quit()
    server.shutdown()
