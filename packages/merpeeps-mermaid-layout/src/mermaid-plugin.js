// A mermaid layout plugin that draws diagrams at their stored Merpeeps positions.
//
// This is the piece that makes stored layout matter outside our own editor.
// Register it once, and any mermaid-based viewer honours the arrangement a
// human made, instead of re-deriving one at render time.
//
// How it has to work, and why:
//
// A registered layout algorithm receives a `LayoutData`, not the source text,
// and mermaid deliberately drops unknown frontmatter keys before that point —
// verified, not assumed (see oracle/layout-plugin-probe.mjs: `configHasMerpeeps`
// and `layoutDataHasMerpeeps` are both false). So the algorithm cannot read the Merpeeps
// block itself. What it *does* get is `diagramId`, and the caller knows both
// the id and the text. `renderWithLayout` bridges that gap: it reads the
// positions, parks them under the id, and hands the id to mermaid.
//
// Selecting this algorithm is done in memory, never written to disk, so the
// file on disk stays an ordinary mermaid document that any stock viewer draws
// normally.

import * as yaml from 'js-yaml';
import { getPositions, getStyles, split, parse } from './index.js';
import { replayRenderedGeometry } from './rendered-geometry.js';

/** The name this algorithm registers under (`config.layout`). */
export const LAYOUT_NAME = 'merpeeps';

/** Layout and styling parked for an in-flight render, keyed by diagram id. */
const pending = new Map();

/**
 * Register the Merpeeps layout algorithm with a mermaid instance.
 *
 * ```js
 * import mermaid from 'mermaid';
 * import { registerMerpeepsLayout, renderWithLayout } from 'merpeeps-mermaid-layout/mermaid';
 *
 * registerMerpeepsLayout(mermaid);
 * const { svg } = await renderWithLayout(mermaid, 'g1', source);
 * ```
 */
export function registerMerpeepsLayout(mermaid) {
  mermaid.registerLayoutLoaders([
    { name: LAYOUT_NAME, loader: async () => ({ render }) },
  ]);
  return mermaid;
}

/**
 * Render `text`, honouring any positions stored in its Merpeeps block.
 *
 * Falls straight through to `mermaid.render` when the document stores no
 * positions, or when it uses subgraphs — see the note on groups below. In both
 * cases you get mermaid's normal output, never a broken diagram.
 */
export async function renderWithLayout(mermaid, id, text, container) {
  const positions = getPositions(text);
  const styles = getStyles(text);
  const rendered = parse(text)?.layout?.rendered;
  if (rendered) {
    const result = await mermaid.render(id, text, container);
    const styled = styleSvg(result.svg, styles);
    return { ...result, svg: replayRenderedGeometry(styled, rendered, positions, id) };
  }

  const hasPositions = Object.keys(positions).length > 0;
  const hasStyles = Object.keys(styles).length > 0;

  // Nothing stored, or a shape this algorithm does not handle: let mermaid do
  // what it always does.
  if (!hasPositions || hasGroups(text)) {
    // Styling alone is still worth applying, and needs no custom layout.
    const result = await mermaid.render(id, text, container);
    return hasStyles ? { ...result, svg: styleSvg(result.svg, styles) } : result;
  }

  pending.set(id, { positions, styles });
  try {
    return await mermaid.render(id, selectLayout(text), container);
  } finally {
    pending.delete(id);
  }
}

// ------------------------------------------------------------- the algorithm

async function render(data4Layout, svg, helpers) {
  const {
    insertCluster, insertEdge, insertEdgeLabel, insertMarkers, insertNode,
    positionEdgeLabel,
  } = helpers;

  const { positions: stored = {}, styles = {} } =
    pending.get(data4Layout.diagramId) ?? {};
  const nodeDb = {};
  const clusterDb = {};

  const element = svg.select('g');
  insertMarkers(element, data4Layout.markers, data4Layout.type, data4Layout.diagramId);
  const subGraphsEl = element.insert('g').attr('class', 'subgraphs');
  const edgePaths = element.insert('g').attr('class', 'edgePaths');
  const edgeLabels = element.insert('g').attr('class', 'edgeLabels');
  const nodesEl = element.insert('g').attr('class', 'nodes');

  // Insert first, then measure. A node's real size depends on its rendered
  // label, so a stored width is a hint rather than the truth.
  //
  // The node objects are used as-is rather than copied: insertNode attaches an
  // `intersect` function to the object it is handed, and insertEdge only clips
  // an edge to the node outline when *both* endpoints expose one. Copying first
  // loses it, and the arrowhead then lands on the node's centre — hidden
  // underneath it, since nodes are drawn after edges.
  await Promise.all(data4Layout.nodes.map(async (node) => {
    if (node.isGroup) {
      clusterDb[node.id] = node;
      nodeDb[node.id] = node;
      await insertCluster(subGraphsEl, node);
      return;
    }
    nodeDb[node.id] = node;
    const el = await insertNode(nodesEl, node, {
      config: data4Layout.config,
      dir: data4Layout.direction || 'TB',
    });
    const box = el.node().getBBox();
    node.width = box.width;
    node.height = box.height;
    node.domId = el;

    // Shapes that do not supply their own still need to clip somehow.
    if (typeof node.intersect !== 'function') {
      node.intersect = (point) => intersectRect(node, point);
    }

    if (styles[node.id]) applyNodeStyle(el.node(), styles[node.id]);
  }));

  place(data4Layout.nodes, nodeDb, stored);

  // Nodes are drawn centred on the origin, so positioning is a translate.
  for (const node of Object.values(nodeDb)) {
    if (!node.domId) continue;
    node.domId.attr('transform', `translate(${node.x}, ${node.y})`);
  }

  await Promise.all(data4Layout.edges.map(async (edge) => {
    await insertEdgeLabel(edgeLabels, edge);

    const startNode = nodeDb[edge.start ?? ''];
    const endNode = nodeDb[edge.end ?? ''];
    if (!startNode || !endNode) return;

    // insertEdge discards the first and last point and re-derives them by
    // intersecting against each node, so a two-point line leaves it nothing to
    // work from and it throws. A midpoint is the minimum it can use.
    const from = { x: startNode.x ?? 0, y: startNode.y ?? 0 };
    const to = { x: endNode.x ?? 0, y: endNode.y ?? 0 };
    const mid = { x: (from.x + to.x) / 2, y: (from.y + to.y) / 2 };

    const withPath = { ...edge, points: [from, mid, to] };
    const paths = insertEdge(
      edgePaths, withPath, clusterDb, data4Layout.type,
      startNode, endNode, data4Layout.diagramId,
    );
    positionEdgeLabel(withPath, paths);
  }));
}

/**
 * Apply stored coordinates, and give anything unpositioned a predictable spot
 * below the rest rather than stacking it all on the origin.
 *
 * Merpeeps stores the top-left corner; mermaid positions by centre.
 */
function place(nodes, nodeDb, stored) {
  let cursorX = 0;
  let maxY = 0;
  let anyStored = false;

  for (const raw of nodes) {
    const node = nodeDb[raw.id];
    if (!node) continue;
    const p = stored[raw.id];
    if (!p) continue;

    node.x = p.x + (node.width ?? 0) / 2;
    node.y = p.y + (node.height ?? 0) / 2;
    maxY = Math.max(maxY, p.y + (node.height ?? 0));
    cursorX = Math.min(cursorX, p.x);
    anyStored = true;
  }

  const fallbackY = (anyStored ? maxY : 0) + 60;
  let fallbackX = anyStored ? cursorX : 0;

  for (const raw of nodes) {
    const node = nodeDb[raw.id];
    if (!node || stored[raw.id]) continue;

    node.x = fallbackX + (node.width ?? 0) / 2;
    node.y = fallbackY + (node.height ?? 0) / 2;
    fallbackX += (node.width ?? 0) + 40;
  }
}

// ------------------------------------------------------------------ helpers

// ------------------------------------------------------------------ styling

/**
 * Paint one node's outline and label from its stored style.
 *
 * Applied to the rendered SVG rather than pushed through mermaid's own
 * `style`/`classDef` syntax, because writing style into the diagram body would
 * change the file — and Merpeeps exists precisely so presentation can be stored
 * *without* touching what the diagram says.
 *
 * Written as inline style properties with `important`, since mermaid's
 * stylesheet targets the same elements by class and would otherwise win.
 */
export function applyNodeStyle(nodeEl, style) {
  if (!nodeEl || !style) return;

  const shapes = nodeEl.querySelectorAll(
    'rect, circle, ellipse, polygon, path.basic, path.outer-path, path.node-bkg, path');

  for (const shape of shapes) {
    // Arrow markers and label glyphs live inside the node group too; painting
    // them would smear the fill across text.
    if (shape.closest('.label') || shape.closest('foreignObject')) continue;

    if (style.fill) shape.style.setProperty('fill', style.fill, 'important');
    if (style.stroke) shape.style.setProperty('stroke', style.stroke, 'important');
    if (style.strokeWidth != null) {
      shape.style.setProperty('stroke-width', `${style.strokeWidth}px`, 'important');
    }
    if (style.dashed) {
      shape.style.setProperty('stroke-dasharray', '6 4', 'important');
    }
  }

  if (style.text) {
    for (const t of nodeEl.querySelectorAll('text, tspan, .nodeLabel, span, p')) {
      t.style.setProperty('color', style.text, 'important');
      t.style.setProperty('fill', style.text, 'important');
    }
  }
}

/**
 * Apply stored styling to an already-rendered SVG string.
 *
 * Used on the fall-through paths, where mermaid did the layout and there is no
 * plugin hook to paint through. Needs a DOM, so on a server it is a no-op
 * rather than an error — the diagram is still correct, just unstyled.
 */
export function styleSvg(svg, styles) {
  if (typeof document === 'undefined') return svg;

  const host = document.createElement('div');
  host.innerHTML = svg;

  for (const [id, style] of Object.entries(styles)) {
    for (const el of host.querySelectorAll('g.node')) {
      // Mermaid prefixes and suffixes node ids (`flowchart-gw-3`), so match on
      // the id as a whole segment rather than by substring.
      if (!new RegExp(`(^|[-_])${escapeRe(id)}([-_]|$)`).test(el.id)) continue;
      applyNodeStyle(el, style);
    }
  }
  return host.innerHTML;
}

function escapeRe(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Where the line from `point` to the node's centre crosses the node's box.
 *
 * Used only for shapes that do not attach their own `intersect`. This is the
 * classic dagre-d3 rectangle intersection.
 */
function intersectRect(node, point) {
  const dx = point.x - node.x;
  const dy = point.y - node.y;
  let w = (node.width ?? 0) / 2;
  let h = (node.height ?? 0) / 2;

  let sx;
  let sy;
  if (Math.abs(dy) * w > Math.abs(dx) * h) {
    if (dy < 0) h = -h;
    sx = dy === 0 ? 0 : (h * dx) / dy;
    sy = h;
  } else {
    if (dx < 0) w = -w;
    sx = w;
    sy = dx === 0 ? 0 : (w * dy) / dx;
  }
  return { x: node.x + sx, y: node.y + sy };
}

/**
 * Add `config.layout: merpeeps` to a copy of the source.
 *
 * Deliberately in memory only. Writing it to the file would make the document
 * depend on this plugin being installed, which is exactly the lock-in Merpeeps
 * exists to avoid.
 */
function selectLayout(text) {
  const { fm, body } = split(text);
  const next = {
    ...fm,
    config: { ...(fm.config ?? {}), layout: LAYOUT_NAME },
  };
  const head = yaml.dump(next, { lineWidth: -1, noRefs: true }).replace(/\s*$/, '');
  return `---\n${head}\n---\n${body}`;
}

/**
 * Subgraphs need cluster bounds derived from their children, which stored
 * node positions alone do not determine. Rather than draw them wrongly, hand
 * those diagrams back to mermaid's own layout.
 */
function hasGroups(text) {
  return /^\s*subgraph\s/m.test(split(text).body);
}
