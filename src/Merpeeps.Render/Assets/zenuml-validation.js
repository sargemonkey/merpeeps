async function registerZenUmlValidation(engine) {
  const exported = globalThis.zenuml;
  const ZenUml = typeof exported === 'function' ? exported : exported?.default;
  const version = document.documentElement.dataset.zenumlVersion;
  if (typeof ZenUml !== 'function' || ZenUml.version !== version) {
    throw new Error('The packaged ZenUML parser is missing or has an unexpected version.');
  }
  const host = document.getElementById('zenuml-parser');
  const instance = new ZenUml(host, true);

  await engine.registerExternalDiagrams([{
    id: 'zenuml',
    detector: source => /^\s*zenuml(?:\s|$)/.test(source),
    loader: async () => ({
      id: 'zenuml',
      diagram: {
        db: { clear() {} },
        parser: {
          async parse(source) {
            const code = source.replace(/^\s*zenuml\b/, '');
            const parsed = await instance.parse(code);
            if (!parsed || typeof parsed.pass !== 'boolean' || !Array.isArray(parsed.errorDetails)) {
              const error = new Error('ZenUML returned an invalid parser response.');
              error.name = 'MermaidParserUnavailableError';
              throw error;
            }
            if (parsed.pass && parsed.errorDetails.length === 0) return;
            if (parsed.errorDetails.length === 0) {
              const error = new Error('ZenUML could not parse the source and returned no diagnostic.');
              error.name = 'MermaidParserUnavailableError';
              throw error;
            }
            const error = new Error(parsed.errorDetails.map(detail =>
              `Line ${detail.line}, column ${detail.column + 1}: ${detail.msg}`).join('\n'));
            error.name = 'ZenUmlSyntaxError';
            error.hash = { loc: {
              first_line: parsed.errorDetails[0].line,
              first_column: parsed.errorDetails[0].column,
            } };
            throw error;
          },
        },
        renderer: {
          draw() {
            throw new Error('The ZenUML validation adapter does not render diagrams.');
          },
        },
      },
    }),
  }]);
}
