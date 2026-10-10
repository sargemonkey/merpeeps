export interface DiagramBounds { x: number; y: number; w: number; h: number }
export interface RenderedTextLine extends DiagramBounds { text: string }
export interface RenderedText {
  fontFamily: string;
  fontSize: number;
  fontWeight: number;
  lines: RenderedTextLine[];
  background?: string | null;
}
export interface RenderedNode extends DiagramBounds { text: string; label?: RenderedText | null }
export interface RenderedGroup extends DiagramBounds {
  text: string;
  members: string[];
  fill?: string | null;
  stroke?: string | null;
  strokeWidth?: number;
  label?: RenderedText | null;
}
export interface RenderedEdge { points: Array<[number, number]>; text: string; label?: RenderedText | null }
export interface RenderedGraphGeometry {
  spec: 1;
  nodes: Record<string, RenderedNode>;
  groups: Record<string, RenderedGroup>;
  edges: Record<string, RenderedEdge>;
  caption?: { label: RenderedText | null } | null;
}
/** Connected browser SVG required; captures diagram units, not screen pixels. */
export declare function harvestRenderedGeometry(svg: SVGSVGElement, renderId?: string, sampleStep?: number): RenderedGraphGeometry;
export declare function harvestTextGeometry(svg: SVGSVGElement, element: Element | null): RenderedText | null;
/** Browser DOM required; throws on incompatible/stale snapshot rather than claiming fidelity. */
export declare function replayRenderedGeometry(
  svgText: string,
  geometry: RenderedGraphGeometry,
  positions: Record<string, DiagramBounds>,
  renderId?: string,
  styles?: Record<string, { text?: string }>,
): string;
