import type { ToolActivityStripProps } from '../../types';

const STATUS_STYLE: Record<string, string> = {
  running: 'border-orion-cyan/40 text-orion-cyan animate-pulse',
  ok: 'border-emerald-400/40 text-emerald-300',
  failed: 'border-red-400/40 text-red-300',
};

const STATUS_ICON: Record<string, string> = {
  running: '◌',
  ok: '✓',
  failed: '✕',
};

/**
 * Montre ce qu'ORION FAIT pendant qu'il répond — les outils qu'il déclenche et leur issue.
 * Sans cette trace, une action réussie et une action jamais tentée sont indiscernables.
 */
export const ToolActivityStrip = ({ tools }: ToolActivityStripProps) => {
  if (tools.length === 0) return null;

  return (
    <div className="absolute top-20 left-4 right-4 z-30 flex flex-col gap-1.5 pointer-events-none">
      {tools.map((activity, index) => (
        <div
          key={`${activity.tool}-${index}`}
          className={`self-start rounded-lg border bg-black/40 backdrop-blur-md px-3 py-1.5 text-xs font-mono ${
            STATUS_STYLE[activity.status] ?? ''
          }`}
        >
          <span className="mr-2">{STATUS_ICON[activity.status] ?? '·'}</span>
          <span>{activity.tool}</span>
          {activity.status === 'failed' && activity.summary && (
            <span className="ml-2 opacity-70">{activity.summary.slice(0, 80)}</span>
          )}
        </div>
      ))}
    </div>
  );
};

export default ToolActivityStrip;
