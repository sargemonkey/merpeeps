(() => {
  const results = new Map();
  let startupError = null;
  let ready = false;
  const engine = globalThis.mermaid
    ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid?.default
    ?? globalThis.__esbuild_esm_mermaid_nm?.mermaid;
  const version = document.documentElement.dataset.mermaidVersion;
  const initialize = () => engine.initialize({
    startOnLoad: false, securityLevel: 'strict', suppressErrorRendering: true,
    layout: 'dagre', theme: 'default', look: 'classic',
  });

  const startup = (async () => {
    if (typeof engine?.parse !== 'function') {
      throw new Error('The packaged Mermaid parser is unavailable.');
    }
    initialize();
    await registerZenUmlValidation(engine);
  })().catch(error => {
    startupError = String(error?.message ?? error);
  }).finally(() => { ready = true; });
  let queue = startup;

  function result(requestId, status, diagramType = null, diagnostics = []) {
    return { requestId, mermaidVersion: version, status, diagramType, diagnostics };
  }

  function diagnostic(code, message, error) {
    const location = error?.hash?.loc;
    const parserLine = Number.isInteger(location?.first_line) && location.first_line > 0
      ? location.first_line : null;
    const parserColumn = Number.isInteger(location?.first_column) && location.first_column >= 0
      ? location.first_column + 1 : null;
    return { code, message, parserLine, parserColumn };
  }

  function validate(requestId, source) {
    if (typeof requestId !== 'string' || !requestId.trim() || typeof source !== 'string') {
      throw new TypeError('Validation requires a nonempty request ID and a source string.');
    }
    if (results.has(requestId)) throw new Error('Validation request ID is already in use.');
    if (results.size >= 32) throw new Error('Consume or cancel previous validation requests first.');
    results.set(requestId, result(requestId, 'pending'));

    queue = queue.then(async () => {
      if (!results.has(requestId)) return;
      let response;
      if (startupError) {
        response = result(requestId, 'unavailable', null,
          [diagnostic('engine-unavailable', startupError)]);
      } else {
        try {
          initialize();
          const parsed = await engine.parse(source, { suppressErrors: false });
          if (!parsed || typeof parsed.diagramType !== 'string') {
            response = result(requestId, 'unavailable', null,
              [diagnostic('invalid-engine-response', 'Mermaid returned no parsed diagram type.')]);
          } else {
            response = result(requestId, 'valid', parsed.diagramType);
          }
        } catch (error) {
          const name = error?.name ?? '';
          const status = name === 'UnknownDiagramError' ? 'unsupported'
            : ['TypeError', 'ReferenceError', 'RangeError', 'MermaidParserUnavailableError'].includes(name) ? 'unavailable'
            : 'invalid';
          response = result(requestId, status, null, [
            diagnostic(status === 'unsupported' ? 'unknown-diagram'
              : status === 'unavailable' ? 'engine-error' : 'syntax-error',
            String(error?.message ?? error), error),
          ]);
        }
      }
      if (results.has(requestId)) results.set(requestId, response);
    });
    return true;
  }

  window.merpeepsValidation = Object.freeze({
    get ready() { return ready; },
    get available() { return ready && startupError === null; },
    version,
    zenUmlVersion: document.documentElement.dataset.zenumlVersion,
    validate,
    take(requestId) {
      if (!results.has(requestId)) throw new Error('Unknown or consumed validation request.');
      const response = results.get(requestId);
      if (response.status !== 'pending') results.delete(requestId);
      return response;
    },
    cancel(requestId) {
      return results.delete(requestId);
    },
  });
})();
