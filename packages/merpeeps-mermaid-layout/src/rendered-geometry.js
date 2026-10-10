const nodeId = (element, renderId) => element.id
  .replace(new RegExp(`^${escapeRegex(renderId)}-flowchart-`), '').replace(/-\d+$/, '');
const groupId = (element, renderId) => element.id.replace(new RegExp(`^${escapeRegex(renderId)}-`), '');
const escapeRegex = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
const textOf = element => (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
const rect = (x, y, w, h) => ({ x, y, w, h });

function svgBounds(svg, element) {
  const b = element.getBBox();
  const m = svg.getScreenCTM().inverse().multiply(element.getScreenCTM());
  const corners = [[b.x, b.y], [b.x + b.width, b.y], [b.x, b.y + b.height], [b.x + b.width, b.y + b.height]]
    .map(([x, y]) => new DOMPoint(x, y).matrixTransform(m));
  const x = Math.min(...corners.map(p => p.x)), y = Math.min(...corners.map(p => p.y));
  return rect(x, y, Math.max(...corners.map(p => p.x)) - x, Math.max(...corners.map(p => p.y)) - y);
}

function color(value) {
  const match = /^rgba?\(([\d.,\s]+)\)$/.exec(value);
  if (!match) return value === 'none' ? '#00000000' : value;
  const channels = match[1].split(',').map(Number);
  return '#' + channels.slice(0, 3).map(v => Math.round(v).toString(16).padStart(2, '0')).join('')
    + (channels.length === 4 ? Math.round(channels[3] * 255).toString(16).padStart(2, '0') : '');
}

function labelGeometry(svg, element) {
  if (!element) return null;
  const inverse = svg.getScreenCTM().inverse();
  const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
  const words = [];
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    for (const match of node.textContent.matchAll(/\S+/g)) {
      const range = document.createRange();
      range.setStart(node, match.index); range.setEnd(node, match.index + match[0].length);
      const b = range.getBoundingClientRect();
      if (b.width <= 0 || b.height <= 0) continue;
      const a = new DOMPoint(b.left, b.top).matrixTransform(inverse);
      const z = new DOMPoint(b.right, b.bottom).matrixTransform(inverse);
      words.push({ text: match[0], x: a.x, y: a.y, w: z.x - a.x, h: z.y - a.y });
    }
  }
  const lines = [];
  for (const word of words) {
    let line = lines.find(l => Math.abs(l.y - word.y) < 1);
    if (!line) { line = { ...word }; lines.push(line); }
    else { line.text += ' ' + word.text; line.w = word.x + word.w - line.x; line.h = Math.max(line.h, word.h); }
  }
  const style = getComputedStyle(element.querySelector('p,span,text') ?? element);
  const weight = style.fontWeight === 'bold' ? 700 : style.fontWeight === 'normal' ? 400 : Number(style.fontWeight);
  const background = getComputedStyle(element.querySelector('.labelBkg') ?? element).backgroundColor;
  return { fontFamily: style.fontFamily, fontSize: parseFloat(style.fontSize), fontWeight: weight, lines,
    background: background && background !== 'rgba(0, 0, 0, 0)' ? color(background) : null };
}
export { labelGeometry as harvestTextGeometry };

/** Capture the currently visible Flowchart, without invoking another layout. */
export function harvestRenderedGeometry(svg, renderId = 'merpeeps-preview', sampleStep = 2) {
  if (!svg?.getScreenCTM()) throw new Error('A connected rendered SVG is required.');
  if (!(sampleStep > 0 && Number.isFinite(sampleStep))) throw new Error('The path sample step must be positive.');
  const nodes = {}, groups = {}, edges = {};
  for (const node of svg.querySelectorAll('g.node')) {
    const id = nodeId(node, renderId);
    const bounds = svgBounds(svg, node);
    if (bounds.w <= 0 || bounds.h <= 0) throw new Error(`Cannot measure node ${id}.`);
    nodes[id] = { ...bounds, text: node.getAttribute('aria-label') ?? textOf(node.querySelector('.nodeLabel')), label: labelGeometry(svg, node.querySelector('.label')) };
  }
  for (const group of svg.querySelectorAll('g.cluster')) {
    const id = groupId(group, renderId), shape = group.querySelector(':scope > rect');
    if (!shape) throw new Error(`Cannot measure group ${id}.`);
    const bounds = svgBounds(svg, shape), style = getComputedStyle(shape);
    const members = Object.entries(nodes).filter(([, n]) => n.x + n.w / 2 >= bounds.x && n.x + n.w / 2 <= bounds.x + bounds.w
      && n.y + n.h / 2 >= bounds.y && n.y + n.h / 2 <= bounds.y + bounds.h).map(([id]) => id);
    groups[id] = { ...bounds, text: textOf(group.querySelector('.cluster-label')), members,
      fill: color(style.fill), stroke: color(style.stroke), strokeWidth: parseFloat(style.strokeWidth),
      label: labelGeometry(svg, group.querySelector('.cluster-label')) };
  }
  for (const path of svg.querySelectorAll('path[data-edge="true"]')) {
    const rawId = path.getAttribute('data-id') ?? '';
    const matches = [];
    for (const from of Object.keys(nodes)) for (const to of Object.keys(nodes)) {
      const match = new RegExp(`^L_${escapeRegex(from)}_${escapeRegex(to)}_(\\d+)$`).exec(rawId);
      if (match) matches.push({ from, to, ordinal: Number(match[1]) });
    }
    if (matches.length !== 1) throw new Error(`Connector identity ${rawId} is ambiguous or unsupported.`);
    const { from, to, ordinal } = matches[0];
    const key = `${from}->${to}#${ordinal}`;
    const matrix = svg.getScreenCTM().inverse().multiply(path.getScreenCTM());
    const length = path.getTotalLength(), count = Math.max(1, Math.ceil(length / sampleStep));
    const points = Array.from({ length: count + 1 }, (_, i) => {
      const p = path.getPointAtLength(length * i / count).matrixTransform(matrix);
      return [p.x, p.y];
    });
    const label = [...svg.querySelectorAll('g.edgeLabel > g.label')].find(g => g.getAttribute('data-id') === rawId);
    edges[key] = { points, text: textOf(label), label: labelGeometry(svg, label) };
  }
  const title = svg.querySelector('.flowchartTitleText');
  return { spec: 1, nodes, groups, edges, caption: title ? { label: labelGeometry(svg, title) } : null };
}

function rgba(css) {
  const probe = document.createElement('span');
  probe.style.color = css === 'none' ? 'transparent' : css;
  if (!probe.style.color) throw new Error(`Cannot resolve color ${css}.`);
  document.body.append(probe);
  const value = getComputedStyle(probe).color;
  probe.remove();
  const match = /rgba?\(([^)]+)\)/.exec(value);
  if (!match) throw new Error(`Cannot resolve color ${css}.`);
  const parts = match[1].split(',').map(Number);
  return [parts[0], parts[1], parts[2], parts[3] ?? 1];
}
function composite(top, bottom) {
  const a = rgba(top), b = rgba(bottom);
  return `rgb(${a.slice(0, 3).map((v, i) => Math.round(v * a[3] + b[i] * (1 - a[3]))).join(',')})`;
}
function ratio(a, b) {
  const luminance = css => rgba(css).slice(0, 3).map(v => {
    const c = v / 255; return c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4;
  }).reduce((sum, v, i) => sum + v * [.2126, .7152, .0722][i], 0);
  const x = luminance(a), y = luminance(b);
  return (Math.max(x, y) + .05) / (Math.min(x, y) + .05);
}
function readable(ink, surface, threshold = 4.5) {
  if (ratio(composite(ink, surface), surface) >= threshold) return ink;
  return ratio('#000000', surface) >= ratio('#ffffff', surface) ? '#000000' : '#ffffff';
}
function renderText(label, ink, offset = { x: 0, y: 0 }, surface = '#ffffff', authored = false) {
  const group = document.createElementNS('http://www.w3.org/2000/svg', 'g');
  if (!authored) ink = readable(ink, label?.background ? composite(label.background, surface) : surface);
  if (label?.background && label.lines.length) {
    const x = Math.min(...label.lines.map(l => l.x)) + offset.x - 3, y = Math.min(...label.lines.map(l => l.y)) + offset.y - 2;
    const right = Math.max(...label.lines.map(l => l.x + l.w)) + offset.x + 3, bottom = Math.max(...label.lines.map(l => l.y + l.h)) + offset.y + 2;
    const box = document.createElementNS(group.namespaceURI, 'rect');
    for (const [key, value] of Object.entries({ x, y, width: right - x, height: bottom - y, fill: label.background })) box.setAttribute(key, String(value));
    group.append(box);
  }
  for (const line of label?.lines ?? []) {
    const text = document.createElementNS(group.namespaceURI, 'text');
    text.textContent = line.text;
    const attrs = { x: line.x + line.w / 2 + offset.x, y: line.y + offset.y, 'text-anchor': 'middle',
      'dominant-baseline': 'text-before-edge', 'font-family': label.fontFamily, 'font-size': label.fontSize,
      'font-weight': label.fontWeight, fill: ink };
    for (const [key, value] of Object.entries(attrs)) text.setAttribute(key, String(value));
    text.style.fontFamily = label.fontFamily;
    text.style.fontSize = `${label.fontSize}px`;
    text.style.fontWeight = String(label.fontWeight);
    text.style.fill = ink;
    group.append(text);
  }
  return group;
}

/** Replay a complete imported snapshot, retaining Mermaid's native node shapes. */
export function replayRenderedGeometry(svgText, geometry, positions, renderId = 'merpeeps-preview', styles = {}) {
  if (geometry?.spec !== 1) throw new Error('Unsupported rendered geometry version.');
  const host = document.createElement('div');
  host.style.cssText = 'position:fixed;left:-100000px;top:0;visibility:hidden;width:2200px';
  host.innerHTML = svgText; document.body.append(host);
  try {
    const svg = host.querySelector('svg');
    const current = harvestRenderedGeometry(svg, renderId);
    for (const [id, node] of Object.entries(geometry.nodes)) {
      if (!current.nodes[id] || current.nodes[id].text !== node.text) throw new Error(`Rendered snapshot is stale for node ${id}. Re-adopt geometry after changing content.`);
    }
    const root = document.createElementNS(svg.namespaceURI, 'g');
    root.setAttribute('class', 'rendered-geometry');
    const canvas = window.merpeepsCanvasColor ?? composite(getComputedStyle(document.body).backgroundColor, '#ffffff');
    const inherited = readable('#333333', canvas);
    const deltas = Object.fromEntries(Object.entries(geometry.nodes).map(([id, n]) => {
      const p = positions[id] ?? n;
      if (Math.abs(p.w - n.w) > .02 || Math.abs(p.h - n.h) > .02) throw new Error(`Rendered snapshot is stale for the size of ${id}.`);
      return [id, { x: p.x - n.x, y: p.y - n.y }];
    }));
    const frames = Object.entries(geometry.groups).map(([id, group]) => {
      const delta = deltas[group.members[0]] ?? { x: 0, y: 0 };
      if (group.members.some(member => !deltas[member] || Math.abs(deltas[member].x - delta.x) > .02 || Math.abs(deltas[member].y - delta.y) > .02))
        throw new Error(`Group ${id} changed internally. Re-adopt geometry to preserve reference composition.`);
      return { id, group, delta, x: group.x + delta.x, y: group.y + delta.y };
    }).sort((a, b) => b.group.w * b.group.h - a.group.w * a.group.h);
    const surfaceAt = point => frames.filter(f =>
      point[0] >= f.x && point[1] >= f.y && point[0] <= f.x + f.group.w && point[1] <= f.y + f.group.h)
      .reduce((surface, f) => composite(f.group.fill ?? 'transparent', surface), canvas);
    root.append(renderText(geometry.caption?.label, inherited, undefined, canvas));
    for (const { id, group, delta } of frames) {
      const existing = [...svg.querySelectorAll('g.cluster')].find(g => groupId(g, renderId) === id);
      if (!existing || current.groups[id].text !== group.text ||
          current.groups[id].members.slice().sort().join('\0') !== group.members.slice().sort().join('\0'))
        throw new Error(`Rendered snapshot is stale for group ${id}.`);
      const clone = document.createElementNS(svg.namespaceURI, 'g'); clone.setAttribute('class', 'cluster'); clone.id = existing.id;
      const box = document.createElementNS(svg.namespaceURI, 'rect');
      for (const [key, value] of Object.entries({ x: group.x + delta.x, y: group.y + delta.y, width: group.w, height: group.h,
        fill: group.fill ?? 'none', stroke: group.stroke ?? '#999999', 'stroke-width': group.strokeWidth ?? 1 })) box.setAttribute(key, String(value));
      box.style.setProperty('fill', group.fill ?? 'none', 'important');
      box.style.setProperty('stroke', group.stroke ?? '#999999', 'important');
      const labelPoint = group.label?.lines[0] ?? { x: group.x, y: group.y };
      clone.append(box, renderText(group.label, inherited, delta, surfaceAt([labelPoint.x + delta.x, labelPoint.y + delta.y]))); root.append(clone);
    }
    for (const path of svg.querySelectorAll('.edgePaths path[data-edge="true"]')) {
      const rawId = path.getAttribute('data-id');
      const entry = Object.entries(geometry.edges).find(([key]) => {
        const split = /^(.*)->(.*)#(\d+)$/.exec(key);
        return rawId === `L_${split[1]}_${split[2]}_${split[3]}`;
      });
      if (!entry) continue;
      const [key, edge] = entry, [, from, to] = /^(.*)->(.*)#\d+$/.exec(key);
      if (current.edges[key]?.text !== edge.text) throw new Error(`Rendered snapshot is stale for connector ${key}.`);
      const delta = deltas[from];
      if (!delta || !deltas[to] || Math.abs(delta.x - deltas[to].x) > .02 || Math.abs(delta.y - deltas[to].y) > .02)
        throw new Error(`Connector ${key} changed endpoints. Re-adopt geometry.`);
      const clone = path.cloneNode(true); clone.removeAttribute('transform');
      clone.setAttribute('d', edge.points.map(([x, y], i) => `${i ? 'L' : 'M'}${x + delta.x},${y + delta.y}`).join(' '));
      const center = edge.points[Math.floor(edge.points.length / 2)].map((v, i) => v + [delta.x, delta.y][i]);
      const ink = readable(getComputedStyle(path).stroke, surfaceAt(center), 3);
      clone.style.stroke = ink;
      if (edge.points.some(p => ratio(ink, surfaceAt([p[0] + delta.x, p[1] + delta.y])) < 3)) {
        const halo = clone.cloneNode(true);
        halo.removeAttribute('data-edge');
        halo.removeAttribute('id');
        halo.setAttribute('data-contrast-halo', 'true');
        halo.removeAttribute('marker-end'); halo.removeAttribute('marker-start');
        halo.style.stroke = readable(ink, ink, 3);
        halo.style.strokeWidth = String(parseFloat(getComputedStyle(path).strokeWidth) + 2);
        root.append(halo);
      }
      for (const attribute of ['marker-start', 'marker-end']) {
        const markerUrl = clone.getAttribute(attribute);
        const markerId = markerUrl && /#([^)]*)/.exec(markerUrl)?.[1];
        const originalMarker = markerId && svg.querySelector(`[id="${markerId}"]`);
        if (!originalMarker) continue;
        const marker = originalMarker.cloneNode(true); marker.id += '-readable-' + root.children.length + '-' + attribute;
        const endpoint = edge.points[attribute === 'marker-start' ? 0 : edge.points.length - 1];
        const markerInk = readable(ink, surfaceAt([endpoint[0] + delta.x, endpoint[1] + delta.y]), 3);
        svg.querySelector('defs').append(marker);
        for (const shape of marker.querySelectorAll('path,polygon,circle')) {
          if (getComputedStyle(shape).fill !== 'none') shape.style.fill = markerInk;
          if (getComputedStyle(shape).stroke !== 'none') shape.style.stroke = markerInk;
        }
        clone.setAttribute(attribute, `url(#${marker.id})`);
      }
      const labelPoint = edge.label?.lines[0];
      root.append(clone, renderText(edge.label, inherited, delta, surfaceAt(labelPoint
        ? [labelPoint.x + delta.x, labelPoint.y + delta.y] : center)));
    }
    for (const original of svg.querySelectorAll('g.node')) {
      const id = nodeId(original, renderId), node = geometry.nodes[id];
      if (!node) throw new Error(`Imported snapshot is missing node ${id}.`);
      const clone = original.cloneNode(true), box = original.getBBox(), delta = deltas[id];
      clone.setAttribute('aria-label', node.text);
      const x = node.x + delta.x, y = node.y + delta.y;
      clone.setAttribute('transform', `translate(${x - box.x * node.w / box.width},${y - box.y * node.h / box.height}) scale(${node.w / box.width},${node.h / box.height})`);
      clone.querySelector('.label')?.remove();
      root.append(clone);
      const labelElement = original.querySelector('.nodeLabel') ?? original;
      const ink = getComputedStyle(labelElement).color;
      const shape = original.querySelector('rect, circle, ellipse, polygon, path');
      const beneath = surfaceAt([x + node.w / 2, y + node.h / 2]);
      const surface = shape ? composite(getComputedStyle(shape).fill, beneath) : beneath;
      const authored = Boolean(styles[id]?.text || labelElement.style.color || labelElement.style.fill);
      root.append(renderText(node.label, ink, delta, surface, authored));
    }
    for (const child of [...svg.children]) if (!['defs', 'style'].includes(child.tagName.toLowerCase())) child.remove();
    svg.append(root);
    const bounds = root.getBBox();
    const left = bounds.x - 20, top = bounds.y - 20, right = bounds.x + bounds.width + 20, bottom = bounds.y + bounds.height + 20;
    svg.setAttribute('viewBox', `${left} ${top} ${right - left} ${bottom - top}`);
    svg.setAttribute('width', String(right - left)); svg.setAttribute('height', String(bottom - top));
    svg.style.maxWidth = '100%';
    return svg.outerHTML;
  } finally { host.remove(); }
}
