#!/usr/bin/env python3
"""A smart-HTTP git origin for the harness: `git http-backend` behind basic authentication, taking a token
the way github.com takes GITHUB_TOKEN (user `x-access-token`, the token as the password).

Usage: githttp.py <project-root> <port> <log-file>

Environment:
  GITHTTP_TOKEN       reads and writes
  GITHTTP_READ_TOKEN  reads only: a push is answered 403, as a fork's pull request token is
  GITHTTP_USER        user:password, another identity that reads and writes (the stored-credential case)

A request without a credential this server knows is answered 401 with a Basic challenge, which is what
makes git ask for one. Every request is logged: method, path, status, response bytes, the identity it was
served as, and how many Authorization headers it carried. No secret is ever logged.
"""
import base64, os, subprocess, sys, threading
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler

root, port, log_path = sys.argv[1], int(sys.argv[2]), sys.argv[3]
token = os.environ.get('GITHTTP_TOKEN', '')
read_token = os.environ.get('GITHTTP_READ_TOKEN', '')
other = os.environ.get('GITHTTP_USER', '')
lock = threading.Lock()


def identity(header):
    """(name, can_write) for a known Basic credential, else None."""
    if not header or not header.lower().startswith('basic '):
        return None
    try:
        user, _, password = base64.b64decode(header[6:].strip()).decode('utf-8').partition(':')
    except (ValueError, UnicodeDecodeError):
        return None
    if token and user == 'x-access-token' and password == token:
        return ('token', True)
    if read_token and user == 'x-access-token' and password == read_token:
        return ('read-only-token', False)
    if other and f'{user}:{password}' == other:
        return (user, True)
    return None


class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def log_message(self, *args):
        pass

    def body(self):
        if self.headers.get('Transfer-Encoding', '').lower() == 'chunked':
            data = bytearray()
            while True:
                size = int(self.rfile.readline().split(b';')[0].strip(), 16)
                if size == 0:
                    while self.rfile.readline() not in (b'\r\n', b'\n', b''):
                        pass
                    return bytes(data)
                data += self.rfile.read(size)
                self.rfile.readline()
        length = int(self.headers.get('Content-Length') or 0)
        return self.rfile.read(length) if length else b''

    def reply(self, status, headers, payload, who):
        self.send_response(status)
        for name, value in headers:
            self.send_header(name, value)
        self.send_header('Content-Length', str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)
        with lock, open(log_path, 'a') as log:
            log.write(f'{self.command} {self.path} {status} {len(payload)} {who or "-"} '
                      f'auth-headers={len(self.headers.get_all("Authorization") or [])}\n')

    def serve(self):
        data = self.body()  # read before answering, even a 401, so the connection stays in step
        known = [i for i in map(identity, self.headers.get_all('Authorization') or []) if i]
        if not known:
            return self.reply(401, [('WWW-Authenticate', 'Basic realm="harness"')], b'Unauthorized\n', None)
        who, can_write = known[0]
        path, _, query = self.path.partition('?')
        if not can_write and ('git-receive-pack' in path or 'service=git-receive-pack' in query):
            return self.reply(403, [('Content-Type', 'text/plain')], b'Write access to repository not granted.\n', who)
        env = {'PATH': os.environ['PATH'], 'GIT_PROJECT_ROOT': root, 'GIT_HTTP_EXPORT_ALL': '1',
               'GIT_CONFIG_NOSYSTEM': '1', 'GIT_CONFIG_GLOBAL': os.devnull, 'HOME': root,
               'REQUEST_METHOD': self.command, 'PATH_INFO': path, 'QUERY_STRING': query,
               'CONTENT_TYPE': self.headers.get('Content-Type', ''), 'CONTENT_LENGTH': str(len(data)),
               'REMOTE_USER': who, 'REMOTE_ADDR': '127.0.0.1'}
        for name in ('Content-Encoding', 'Git-Protocol'):
            if self.headers.get(name):
                env['HTTP_' + name.upper().replace('-', '_')] = self.headers[name]
        out = subprocess.run(['git', 'http-backend'], input=data, env=env, capture_output=True).stdout
        head, _, payload = out.partition(b'\r\n\r\n') if b'\r\n\r\n' in out else out.partition(b'\n\n')
        status, headers = 200, []
        for line in head.decode('latin-1').splitlines():
            name, _, value = line.partition(':')
            if name.lower() == 'status':
                status = int(value.strip().split()[0])
            elif name:
                headers.append((name, value.strip()))
        self.reply(status, headers, payload, who)

    do_GET = do_POST = serve


ThreadingHTTPServer.daemon_threads = True
ThreadingHTTPServer(('127.0.0.1', port), Handler).serve_forever()
