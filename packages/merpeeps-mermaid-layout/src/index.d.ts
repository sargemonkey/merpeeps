import type { RenderedGraphGeometry } from './rendered-geometry.js';

/** A node's stored position, in diagram coordinates. */
export interface Position {
  x: number;
  y: number;
  /** Width. Omitted when the renderer's default is fine. */
  w?: number;
  /** Height. Omitted when the renderer's default is fine. */
  h?: number;
}

/** Per-node presentation. All fields optional; unknown fields are preserved. */
export interface NodeStyle {
  fill?: string;
  stroke?: string;
  text?: string;
  strokeWidth?: number;
  dashed?: boolean;
  icon?: string;
  [key: string]: unknown;
}

/** The parsed `x-merpeeps` block. Unknown keys are preserved on write. */
export interface MerpeepsBlock {
  spec?: number;
  layout?: {
    nodes?: Record<string, Position>;
    engine?: string;
    rendered?: RenderedGraphGeometry | null;
    [key: string]: unknown;
  };
  style?: {
    nodes?: Record<string, NodeStyle>;
    [key: string]: unknown;
  };
  [key: string]: unknown;
}

export interface SplitResult {
  /** Raw frontmatter text, or null when the document has none. */
  fmText: string | null;
  /** Parsed frontmatter mapping. Empty when absent or malformed. */
  fm: Record<string, unknown>;
  /** Everything after the frontmatter. */
  body: string;
}

export interface SerialiseOptions {
  /**
   * `'frontmatter'` (default) writes under the `x-merpeeps` key.
   * `'comment'` writes a `%% merpeeps1 {...}` line instead, for hosts that strip
   * frontmatter.
   */
  carrier?: 'frontmatter' | 'comment';
}

export interface SetPositionsOptions {
  /** Free-form note about what produced the layout. Defaults to `'manual'`. */
  engine?: string;
}

export interface ValidationIssue {
  rule: 'parse' | 'shape' | 'spec' | 'layout' | 'body' | 'idempotent';
  message: string;
}

export interface ValidationResult {
  ok: boolean;
  issues: ValidationIssue[];
}

export declare const KEY: 'x-merpeeps';
export declare const SPEC_VERSION: number;

export declare function split(src: string): SplitResult;
export declare function parse(src: string): MerpeepsBlock | null;
export declare function serialise(
  src: string,
  merpeeps: MerpeepsBlock | null,
  options?: SerialiseOptions,
): string;
export declare function inject(src: string, patch: MerpeepsBlock): string;
export declare function strip(src: string): string;
export declare function bodyOf(src: string): string;

export declare function getPositions(src: string): Record<string, Position>;
export declare function setPositions(
  src: string,
  positions: Record<string, Position>,
  options?: SetPositionsOptions,
): string;

export declare function getStyles(src: string): Record<string, NodeStyle>;
export declare function setStyles(
  src: string,
  styles: Record<string, NodeStyle>,
): string;

export declare function staleIds(src: string, knownIds: Iterable<string>): string[];
export declare function validate(src: string): ValidationResult;
