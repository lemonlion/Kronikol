// Shared gzip+base64 decompressor — the ONE definition of decompressGzipBase64 in a report.
// Always included: the context-menu script (emitted for InlineSvg too), the internal-flow popup
// (emitted for every rendering mode) and the deep-search index all call it in modes where the
// BrowserJs render script — its historical home — is absent.
function decompressGzipBase64(base64) {
    var raw = atob(base64);
    var bytes = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    var stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
    return new Response(stream).text();
}
window.decompressGzipBase64 = decompressGzipBase64;
