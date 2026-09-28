"""A smart-HTTP git server for the harness: git http-backend behind a token check, one log line per request.

Usage: githttp.py <repos-root> <port-file> <log-file> <token>
Accepts "Authorization: bearer <token>" or "Authorization: basic base64(x-access-token:<token>)". Every
request is logged with how many Authorization headers it carried and which tokens. A request with no valid
header gets 401. A file named fail-next-upload-pack in a repository's directory makes the next request of a
fetch from it (the ref advertisement included) fail with 500, once.
"""
import base64, http.server, os, socketserver, subprocess, sys, threading

root, port_file, log_file, token = sys.argv[1:5]
lock = threading.Lock()

def token_of(value):
    scheme, _, rest = value.strip().partition(" ")
    if scheme.lower() == "bearer":
        return rest.strip()
    if scheme.lower() == "basic":
        try:
            user, _, secret = base64.b64decode(rest.strip()).decode().partition(":")
            return secret
        except Exception:
            return "<undecodable>"
    return "<" + scheme + ">"

class Handler(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def handle_git(self):
        path, _, query = self.path.partition("?")
        auths = self.headers.get_all("Authorization") or []
        tokens = [token_of(a) for a in auths]
        service = "receive-pack" if "receive-pack" in self.path else "upload-pack"
        with lock, open(log_file, "a") as log:
            log.write(f"{path.lstrip('/').split('/', 1)[0]} {self.command} {service} {path.rsplit('/', 1)[-1]} auth-headers={len(auths)} tokens={','.join(tokens) or '-'}\n")
        body = b""
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            while True:
                size = int(self.rfile.readline().strip(), 16)
                if size == 0:
                    self.rfile.readline()
                    break
                body += self.rfile.read(size)
                self.rfile.readline()
        elif self.headers.get("Content-Length"):
            body = self.rfile.read(int(self.headers["Content-Length"]))
        if token not in tokens:
            self.send_response(401)
            self.send_header("WWW-Authenticate", 'Basic realm="harness"')
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        repo = path.lstrip("/").split("/", 1)[0]
        marker = os.path.join(root, repo, "fail-next-upload-pack")
        if service == "upload-pack" and os.path.exists(marker):
            os.remove(marker)
            with lock, open(log_file, "a") as log:
                log.write("  -> injected 500\n")
            self.send_response(500)
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        env = dict(os.environ, GIT_PROJECT_ROOT=root, GIT_HTTP_EXPORT_ALL="1", REMOTE_USER="harness",
                   REQUEST_METHOD=self.command, PATH_INFO=path, QUERY_STRING=query,
                   CONTENT_TYPE=self.headers.get("Content-Type", ""), CONTENT_LENGTH=str(len(body)))
        if self.headers.get("Content-Encoding"):
            env["HTTP_CONTENT_ENCODING"] = self.headers["Content-Encoding"]
        if self.headers.get("Git-Protocol"):
            env["GIT_PROTOCOL"] = self.headers["Git-Protocol"]
        out = subprocess.run(["git", "http-backend"], input=body, env=env, capture_output=True).stdout
        head, _, payload = out.partition(b"\r\n\r\n")
        status = 200
        headers = []
        for line in head.split(b"\r\n"):
            name, _, value = line.decode("latin-1").partition(":")
            if name.lower() == "status":
                status = int(value.strip().split()[0])
            elif name:
                headers.append((name, value.strip()))
        self.send_response(status)
        for name, value in headers:
            self.send_header(name, value)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    do_GET = handle_git
    do_POST = handle_git

class Server(socketserver.ThreadingMixIn, http.server.HTTPServer):
    daemon_threads = True

server = Server(("127.0.0.1", 0), Handler)
with open(port_file, "w") as f:
    f.write(str(server.server_address[1]))
server.serve_forever()
