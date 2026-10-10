/**
 * Minimal shape of the mermaid API this plugin uses. Declared structurally so
 * the package does not need a dependency on mermaid's own types.
 */
export interface MermaidLike {
  registerLayoutLoaders(loaders: Array<{
    name: string;
    loader: () => Promise<unknown>;
    algorithm?: string;
  }>): void;
  render(
    id: string,
    text: string,
    container?: Element,
  ): Promise<{ svg: string; bindFunctions?: (element: Element) => void }>;
}

export interface RenderResult {
  svg: string;
  bindFunctions?: (element: Element) => void;
}

/** The name this algorithm registers under (`config.layout`). */
export declare const LAYOUT_NAME: 'merpeeps';

/** Register the Merpeeps layout algorithm with a mermaid instance. */
export declare function registerMerpeepsLayout<T extends MermaidLike>(mermaid: T): T;

/**
 * Render `text`, honouring any positions and styling stored in its Merpeeps block.
 *
 * Falls through to `mermaid.render` when the document stores no positions, or
 * when it uses subgraphs without an explicit rendered snapshot.
 * An explicit snapshot replays imported geometry, rejecting stale/incompatible
 * content or arrangements. Stored styling is applied on either path.
 */
export declare function renderWithLayout(
  mermaid: MermaidLike,
  id: string,
  text: string,
  container?: Element,
): Promise<RenderResult>;

/** Per-node presentation, as stored under `style.nodes`. */
export interface NodeStyle {
  fill?: string;
  stroke?: string;
  text?: string;
  strokeWidth?: number;
  dashed?: boolean;
  icon?: string;
  [key: string]: unknown;
}

/** Paint one already-rendered node element from a stored style. */
export declare function applyNodeStyle(nodeEl: Element, style: NodeStyle): void;

/**
 * Apply stored styling to a rendered SVG string.
 * Requires a DOM; returns the input unchanged when there is none.
 */
export declare function styleSvg(
  svg: string,
  styles: Record<string, NodeStyle>,
): string;
