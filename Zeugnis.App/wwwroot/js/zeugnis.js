// Browser-Seite von IKO.Zeugnis.
// Enthält nur rohe Kryptografie und Dateihilfen; das Dateiformat liegt in C# (Zeugnis.Exchange.EnvelopeCodec).
window.zeugnis = (() => {
  const encoder = new TextEncoder();

  async function deriveKey(password, salt, iterations) {
    const material = await crypto.subtle.importKey("raw", encoder.encode(password), "PBKDF2", false, ["deriveKey"]);
    return crypto.subtle.deriveKey(
      { name: "PBKDF2", salt, iterations, hash: "SHA-256" },
      material, { name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
  }

  // Ergebnis = Ciphertext || 16-Byte-Tag (identisch zur .NET-Implementierung).
  async function encrypt(plain, password, salt, nonce, iterations, aad) {
    const key = await deriveKey(password, salt, iterations);
    return new Uint8Array(await crypto.subtle.encrypt(
      { name: "AES-GCM", iv: nonce, additionalData: aad, tagLength: 128 }, key, plain));
  }

  async function decrypt(cipherAndTag, password, salt, nonce, iterations, aad) {
    const key = await deriveKey(password, salt, iterations);
    try {
      return new Uint8Array(await crypto.subtle.decrypt(
        { name: "AES-GCM", iv: nonce, additionalData: aad, tagLength: 128 }, key, cipherAndTag));
    } catch {
      throw new Error("ZEUGNIS_DECRYPT_FAILED");
    }
  }

  function download(bytes, fileName, mime) {
    const blob = new Blob([bytes], { type: mime || "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    a.rel = "noopener";
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }

  const isSecure = () => !!(window.isSecureContext && crypto && crypto.subtle);

  // Warnung beim Schließen/Neuladen des Tabs, solange ungespeicherte Änderungen bestehen.
  let guard = false;
  window.addEventListener("beforeunload", e => { if (guard) { e.preventDefault(); e.returnValue = ""; } });
  const setUnloadGuard = active => { guard = !!active; };

  const scrollToMain = () => {
    const el = document.querySelector(".grading-main");
    if (el && el.getBoundingClientRect().top < 0) el.scrollIntoView({ behavior: "smooth", block: "start" });
  };

  // Verschlüsselte Entwürfe im Gerätespeicher (Inhalt ist bereits mit dem Auftragskennwort verschlüsselt).
  const draftPrefix = "zeugnis.draft.";
  const draft = {
    get: id => { try { return localStorage.getItem(draftPrefix + id); } catch { return null; } },
    set: (id, base64) => {
      try { localStorage.setItem(draftPrefix + id, base64); return true; } catch { return false; }
    },
    remove: id => { try { localStorage.removeItem(draftPrefix + id); } catch { } },
  };

  return { encrypt, decrypt, download, isSecure, setUnloadGuard, scrollToMain, draft };
})();
