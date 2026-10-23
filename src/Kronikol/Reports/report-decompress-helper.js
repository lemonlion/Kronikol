// Shared item reader: the ONE definition of decompressGzipBase64 in a report.
// Always included: the context-menu script (emitted for InlineSvg too), the internal-flow popup
// (emitted for every rendering mode) and the deep-search index all call it in modes where the
// BrowserJs render script — its historical home — is absent.
// A compressed report (plans/HTML_COMPRESSION_PLAN.md 4.3) holds every item raw, and each diagram source as a token
// read from the diagram sources' pieces, which its loader keeps on window.__kronikolFrames: there a value is resolved
// through it, unless it is still gzip and base64 (gzip's magic bytes, H4sI), as a fragment merge took from an older
// data file can be. A plain page (written before, by Kronikol4J, or by kronikol decode) keeps every item in gzip.
function decompressGzipBase64(base64) {
    var frames = window.__kronikolFrames;
    if (frames && !(typeof base64 === 'string' && base64.lastIndexOf('H4sI', 0) === 0)) return frames.item(base64);
    var raw = atob(base64);
    var bytes = new Uint8Array(raw.length);
    for (var i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
    var stream = new ReadableStream({ start: function (c) { c.enqueue(bytes); c.close(); } }).pipeThrough(new DecompressionStream('gzip'));
    return new Response(stream).text();
}
window.decompressGzipBase64 = decompressGzipBase64;
