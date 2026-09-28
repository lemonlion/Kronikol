// Shared clipboard writer, the one copyTextToClipboard of a page. The report's script block carries it for the scenario
// copy button, and the context-menu script carries it too, since other pages (the component diagram page) include that
// script without the report's block; as with decompressGzipBase64, the second definition is the same function.
//
// navigator.clipboard exists only in a secure context: https, localhost and file://. A report served over plain http
// from another machine (a LAN address, an internal host name) has none, and every Copy threw there. Text is then
// copied as a selection is, through a read-only text area, which works on any page from the click that asked for it.
// Returns a promise, resolved once the text is on the clipboard.
function copyTextToClipboard(text) {
    text = String(text == null ? '' : text);
    if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') return navigator.clipboard.writeText(text);
    return new Promise(function (resolve, reject) {
        var area = document.createElement('textarea');
        area.value = text;
        area.setAttribute('readonly', '');
        // Out of sight at the current scroll offset, so selecting it scrolls nothing; 12pt keeps iOS from zooming.
        area.style.cssText = 'position:absolute;left:-9999px;top:' + (window.pageYOffset || 0) + 'px;font-size:12pt;border:0;padding:0;margin:0';
        var focused = document.activeElement;
        var selection = document.getSelection();
        var ranges = [];
        for (var i = 0; selection && i < selection.rangeCount; i++) ranges.push(selection.getRangeAt(i));
        document.body.appendChild(area);
        area.select();
        area.setSelectionRange(0, text.length);
        var copied = false;
        try { copied = document.execCommand('copy'); } catch (e) { copied = false; }
        document.body.removeChild(area);
        if (selection) {
            selection.removeAllRanges();
            for (var r = 0; r < ranges.length; r++) selection.addRange(ranges[r]);
        }
        if (focused && typeof focused.focus === 'function') focused.focus();
        if (copied) resolve(); else reject(new Error('the browser did not copy the text'));
    });
}
window.copyTextToClipboard = copyTextToClipboard;

// Whether this page can put an image on the clipboard: only through navigator.clipboard.write and ClipboardItem, which a
// page without a secure context lacks (and Firefox before 127 lacked everywhere). The menus leave image copies out there.
window.canCopyImagesToClipboard = !!(navigator.clipboard && typeof navigator.clipboard.write === 'function' && typeof ClipboardItem === 'function');
