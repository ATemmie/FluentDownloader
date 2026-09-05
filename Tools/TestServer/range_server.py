"""支持 Range 请求的本地测试服务器（用于验证 FluentDownloader 多线程引擎）"""
import http.server
import os
import re
import socketserver
import time

FILE_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "big.bin")
PORT = 8765
# 每块后休眠秒数：64KB / 0.02s ≈ 3.2MB/s，便于测试暂停/恢复
THROTTLE = 0.2


class RangeHandler(http.server.SimpleHTTPRequestHandler):
    def do_GET(self):
        if self.path.split("?")[0] != "/big.bin":
            self.send_error(404)
            return

        size = os.path.getsize(FILE_PATH)
        range_header = self.headers.get("Range")

        if range_header:
            m = re.match(r"bytes=(\d*)-(\d*)", range_header)
            if not m:
                self.send_error(416)
                return
            start = int(m.group(1)) if m.group(1) else 0
            end = int(m.group(2)) if m.group(2) else size - 1
            end = min(end, size - 1)
            if start > end or start >= size:
                self.send_error(416)
                return
            length = end - start + 1
            self.send_response(206)
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
            self.send_header("Accept-Ranges", "bytes")
            self.send_header("ETag", '"fdl-test-etag"')
            self.send_header("Content-Length", str(length))
            self.send_header("Content-Type", "application/octet-stream")
            self.end_headers()
            with open(FILE_PATH, "rb") as f:
                f.seek(start)
                remaining = length
                while remaining > 0:
                    chunk = f.read(min(64 * 1024, remaining))
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                    remaining -= len(chunk)
                    time.sleep(THROTTLE)
        else:
            self.send_response(200)
            self.send_header("Content-Length", str(size))
            self.send_header("Content-Type", "application/octet-stream")
            self.end_headers()
            with open(FILE_PATH, "rb") as f:
                while True:
                    chunk = f.read(64 * 1024)
                    if not chunk:
                        break
                    self.wfile.write(chunk)

    def log_message(self, fmt, *args):
        print(f"[server] {fmt % args}", flush=True)


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


if __name__ == "__main__":
    if not os.path.exists(FILE_PATH):
        print(f"生成测试文件 {FILE_PATH} (100MB)…", flush=True)
        with open(FILE_PATH, "wb") as f:
            block = os.urandom(1024 * 1024)
            for i in range(100):
                f.write(block)
                f.flush()
    print(f"服务启动: http://127.0.0.1:{PORT}/big.bin", flush=True)
    with Server(("127.0.0.1", PORT), RangeHandler) as httpd:
        httpd.serve_forever()
