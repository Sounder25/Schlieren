import './Flow.css';

export function Flow() {
  return (
    <div className="flow-view">
      <div className="view-placeholder">
        <div className="placeholder-glyph">⟿</div>
        <h2 className="placeholder-title">Gas Flow Topology</h2>
        <p className="placeholder-description">
          Gas flows through opcode channels like fluid through a pipe.
          State mutations are density changes. Divergences are shear lines.
        </p>
        <p className="placeholder-note">Canvas 2D renderer — coming next</p>
      </div>
    </div>
  );
}
