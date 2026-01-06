import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SignalRService, PipelineTimeline, PipelineStage } from '../../../core/services/signalr.service';

type FilterMode = 'all' | 'automations' | 'e2e';

@Component({
  selector: 'app-pipeline-timeline',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="pipeline-container">
      <div class="pipeline-header">
        <div class="pipeline-title">
          <span class="pipeline-icon">📊</span>
          <span>Pipeline Latency</span>
        </div>
        <div class="pipeline-controls">
          <div class="filter-tabs">
            <button
              class="filter-tab"
              [class.active]="filterMode() === 'all'"
              (click)="filterMode.set('all')"
              title="Show all events">
              All
            </button>
            <button
              class="filter-tab"
              [class.active]="filterMode() === 'automations'"
              (click)="filterMode.set('automations')"
              title="Show only events with automations">
              Auto
            </button>
            <button
              class="filter-tab"
              [class.active]="filterMode() === 'e2e'"
              (click)="filterMode.set('e2e')"
              title="Show E2E automation cycles only">
              E2E
            </button>
          </div>
          <span class="timeline-count">{{ filteredTimelines().length }} events</span>
          <button class="pipeline-btn" (click)="copyToJson()" title="Copy to JSON">📋</button>
          <button class="pipeline-btn" (click)="clearTimelines()" title="Clear">🗑️</button>
        </div>
      </div>

      <div class="pipeline-content">
        @if (filteredTimelines().length === 0) {
          <div class="pipeline-empty">
            <span class="pipeline-cursor">▌</span>
            @if (filterMode() === 'e2e') {
              Waiting for automation E2E events (button → light response)...
            } @else if (filterMode() === 'automations') {
              Waiting for automation events...
            } @else {
              Waiting for pipeline events...
            }
          </div>
        } @else {
          @for (timeline of filteredTimelines(); track timeline.id) {
            <div class="timeline-entry"
                 [class.slow]="timeline.totalMs > 200"
                 [class.very-slow]="timeline.totalMs > 500"
                 [class.e2e-timeline]="isE2ETimeline(timeline)">
              <div class="timeline-header">
                <span class="timeline-device">{{ timeline.deviceId }}</span>
                @if (timeline.automationName) {
                  <span class="timeline-automation">⚡ {{ timeline.automationName }}</span>
                }
                @if (isE2ETimeline(timeline)) {
                  <span class="e2e-badge">E2E</span>
                }
                <span class="timeline-time">{{ formatTime(timeline.timestampUtc) }}</span>
                <span class="timeline-total" [class.fast]="timeline.totalMs < 100" [class.slow]="timeline.totalMs > 200">
                  {{ timeline.totalMs | number:'1.0-0' }}ms
                </span>
              </div>

              <div class="timeline-bar">
                @for (stage of timeline.stages; track stage.name) {
                  <div
                    class="stage-segment"
                    [class]="'stage-' + stage.category"
                    [style.left.%]="getStageLeft(stage, timeline)"
                    [style.width.%]="getStageWidth(stage, timeline)"
                    [title]="stage.name + ': ' + (stage.durationMs | number:'1.1-1') + 'ms'"
                  >
                    @if (getStageWidth(stage, timeline) > 8) {
                      <span class="stage-label">{{ stage.durationMs | number:'1.0-0' }}ms</span>
                    }
                  </div>
                }
              </div>

              <div class="timeline-legend">
                @for (stage of timeline.stages; track stage.name) {
                  <span class="legend-item" [class]="'legend-' + stage.category">
                    <span class="legend-dot"></span>
                    {{ formatStageName(stage.name) }}: {{ stage.durationMs | number:'1.1-1' }}ms
                  </span>
                }
              </div>
            </div>
          }
        }
      </div>

      <div class="pipeline-footer">
        <div class="latency-summary">
          @if (avgLatency() !== null) {
            <span class="summary-item">
              Avg: <strong [class.fast]="avgLatency()! < 100" [class.slow]="avgLatency()! > 200">{{ avgLatency() | number:'1.0-0' }}ms</strong>
            </span>
            <span class="summary-item">
              Min: <strong>{{ minLatency() | number:'1.0-0' }}ms</strong>
            </span>
            <span class="summary-item">
              Max: <strong [class.slow]="maxLatency()! > 200">{{ maxLatency() | number:'1.0-0' }}ms</strong>
            </span>
          }
        </div>
        <div class="connection-status" [class.connected]="isConnected()">
          {{ isConnected() ? '● Connected' : '○ Disconnected' }}
        </div>
      </div>
    </div>
  `,
  styles: [`
    .pipeline-container {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 200px;
      background: var(--bg-secondary, #161b22);
      border: 1px solid var(--border-primary, #30363d);
      border-radius: 8px;
      overflow: hidden;
      font-family: 'JetBrains Mono', 'SF Mono', 'Monaco', 'Consolas', monospace;
    }

    .pipeline-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 8px 12px;
      background: var(--bg-tertiary, #21262d);
      border-bottom: 1px solid var(--border-primary, #30363d);
    }

    .pipeline-title {
      display: flex;
      align-items: center;
      gap: 8px;
      color: var(--text-primary, #c9d1d9);
      font-size: 12px;
      font-weight: 600;
    }

    .pipeline-icon {
      font-size: 14px;
    }

    .pipeline-controls {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .timeline-count {
      color: var(--text-secondary, #8b949e);
      font-size: 11px;
    }

    .pipeline-btn {
      padding: 4px 8px;
      background: transparent;
      border: 1px solid var(--border-primary, #30363d);
      border-radius: 4px;
      color: var(--text-secondary, #8b949e);
      cursor: pointer;
      font-size: 12px;
      transition: all 0.15s ease;
    }

    .pipeline-btn:hover {
      background: var(--bg-primary, #0d1117);
      border-color: var(--accent-primary, #22c55e);
    }

    .pipeline-content {
      flex: 1;
      overflow-y: auto;
      padding: 8px 12px;
    }

    .pipeline-empty {
      color: var(--text-secondary, #8b949e);
      font-style: italic;
      font-size: 11px;
    }

    .pipeline-cursor {
      color: var(--accent-primary, #22c55e);
      animation: blink 1s infinite;
    }

    @keyframes blink {
      0%, 50% { opacity: 1; }
      51%, 100% { opacity: 0; }
    }

    .timeline-entry {
      padding: 10px;
      margin-bottom: 8px;
      background: var(--bg-primary, #0d1117);
      border: 1px solid var(--border-primary, #30363d);
      border-radius: 6px;
      transition: border-color 0.15s ease;
    }

    .timeline-entry:hover {
      border-color: var(--border-secondary, #484f58);
    }

    .timeline-entry.slow {
      border-left: 3px solid #fbbf24;
    }

    .timeline-entry.very-slow {
      border-left: 3px solid #ef4444;
    }

    .timeline-header {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-bottom: 8px;
      font-size: 11px;
    }

    .timeline-device {
      color: var(--accent-primary, #22c55e);
      font-weight: 600;
    }

    .timeline-automation {
      color: var(--text-secondary, #8b949e);
    }

    .timeline-time {
      color: var(--text-tertiary, #6e7681);
      margin-left: auto;
      font-variant-numeric: tabular-nums;
    }

    .timeline-total {
      font-weight: 700;
      padding: 2px 6px;
      border-radius: 4px;
      background: rgba(139, 148, 158, 0.2);
      color: var(--text-primary, #c9d1d9);
    }

    .timeline-total.fast {
      background: rgba(34, 197, 94, 0.2);
      color: #22c55e;
    }

    .timeline-total.slow {
      background: rgba(251, 191, 36, 0.2);
      color: #fbbf24;
    }

    .timeline-bar {
      position: relative;
      height: 24px;
      background: var(--bg-tertiary, #21262d);
      border-radius: 4px;
      margin-bottom: 8px;
      overflow: hidden;
    }

    .stage-segment {
      position: absolute;
      height: 100%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 9px;
      font-weight: 600;
      color: rgba(255, 255, 255, 0.9);
      transition: opacity 0.15s ease;
      min-width: 2px;
    }

    .stage-segment:hover {
      opacity: 0.8;
    }

    .stage-label {
      text-shadow: 0 1px 2px rgba(0, 0, 0, 0.5);
    }

    /* Stage colors by category */
    .stage-signal {
      background: linear-gradient(135deg, #8b5cf6, #6366f1);
    }

    .stage-db {
      background: linear-gradient(135deg, #3b82f6, #2563eb);
    }

    .stage-broadcast {
      background: linear-gradient(135deg, #06b6d4, #0891b2);
    }

    .stage-automation {
      background: linear-gradient(135deg, #22c55e, #16a34a);
    }

    .stage-mqtt {
      background: linear-gradient(135deg, #f97316, #ea580c);
    }

    .stage-webhook {
      background: linear-gradient(135deg, #ec4899, #db2777);
    }

    .stage-zigbee {
      background: linear-gradient(135deg, #fbbf24, #f59e0b);
    }

    .stage-other {
      background: linear-gradient(135deg, #6b7280, #4b5563);
    }

    .timeline-legend {
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
      font-size: 10px;
    }

    .legend-item {
      display: flex;
      align-items: center;
      gap: 4px;
      color: var(--text-secondary, #8b949e);
    }

    .legend-dot {
      width: 8px;
      height: 8px;
      border-radius: 2px;
    }

    .legend-signal .legend-dot { background: #8b5cf6; }
    .legend-db .legend-dot { background: #3b82f6; }
    .legend-broadcast .legend-dot { background: #06b6d4; }
    .legend-automation .legend-dot { background: #22c55e; }
    .legend-mqtt .legend-dot { background: #f97316; }
    .legend-webhook .legend-dot { background: #ec4899; }
    .legend-zigbee .legend-dot { background: #fbbf24; }
    .legend-other .legend-dot { background: #6b7280; }

    .pipeline-footer {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 6px 12px;
      background: var(--bg-tertiary, #21262d);
      border-top: 1px solid var(--border-primary, #30363d);
      font-size: 11px;
    }

    .latency-summary {
      display: flex;
      gap: 16px;
    }

    .summary-item {
      color: var(--text-secondary, #8b949e);
    }

    .summary-item strong {
      color: var(--text-primary, #c9d1d9);
    }

    .summary-item strong.fast {
      color: #22c55e;
    }

    .summary-item strong.slow {
      color: #fbbf24;
    }

    .connection-status {
      color: var(--text-tertiary, #6e7681);
    }

    .connection-status.connected {
      color: var(--accent-primary, #22c55e);
    }

    .filter-tabs {
      display: flex;
      gap: 2px;
      background: var(--bg-primary, #0d1117);
      border-radius: 4px;
      padding: 2px;
    }

    .filter-tab {
      padding: 3px 8px;
      font-size: 10px;
      background: transparent;
      border: none;
      color: var(--text-secondary, #8b949e);
      cursor: pointer;
      border-radius: 3px;
      transition: all 0.15s ease;
    }

    .filter-tab:hover {
      color: var(--text-primary, #c9d1d9);
      background: var(--bg-tertiary, #21262d);
    }

    .filter-tab.active {
      background: var(--accent-primary, #238636);
      color: white;
    }

    .e2e-badge {
      font-size: 9px;
      padding: 1px 5px;
      background: linear-gradient(135deg, #fbbf24, #f59e0b);
      color: #000;
      border-radius: 3px;
      font-weight: 600;
    }

    .timeline-entry.e2e-timeline {
      border-left: 3px solid #fbbf24;
    }

    .timeline-automation {
      color: #22c55e;
      font-weight: 500;
    }
  `]
})
export class PipelineTimelineComponent implements OnInit, OnDestroy {
  private signalR = inject(SignalRService);

  timelines = this.signalR.pipelineTimelines;
  isConnected = this.signalR.isConnected;

  // Filter mode: all, automations (has automation name or fired automation), e2e (ZigbeeRoundTrip stage)
  filterMode = signal<FilterMode>('all');

  // Filtered timelines based on current filter mode
  filteredTimelines = computed(() => {
    const mode = this.filterMode();
    const all = this.timelines();

    switch (mode) {
      case 'automations':
        // Show events that have an automation name OR have significant automation time (>10ms)
        return all.filter(t =>
          t.automationName ||
          t.stages.some(s => s.name === 'Automation' && s.durationMs > 10) ||
          this.isE2ETimeline(t)
        );
      case 'e2e':
        // Show only E2E timelines (have ZigbeeRoundTrip stage - these are complete automation cycles)
        return all.filter(t => this.isE2ETimeline(t));
      default:
        return all;
    }
  });

  avgLatency = computed(() => {
    const t = this.filteredTimelines();
    if (t.length === 0) return null;
    return t.reduce((sum, tl) => sum + tl.totalMs, 0) / t.length;
  });

  minLatency = computed(() => {
    const t = this.filteredTimelines();
    if (t.length === 0) return null;
    return Math.min(...t.map(tl => tl.totalMs));
  });

  maxLatency = computed(() => {
    const t = this.filteredTimelines();
    if (t.length === 0) return null;
    return Math.max(...t.map(tl => tl.totalMs));
  });

  // Check if timeline is an E2E automation cycle (has ZigbeeRoundTrip stage)
  isE2ETimeline(timeline: PipelineTimeline): boolean {
    return timeline.stages.some(s => s.name === 'ZigbeeRoundTrip' || s.name === 'RuleLookup');
  }

  async ngOnInit() {
    await this.signalR.connect();
    await this.signalR.subscribeToPipelineTimelines();
  }

  async ngOnDestroy() {
    await this.signalR.unsubscribeFromPipelineTimelines();
  }

  clearTimelines(): void {
    this.signalR.clearPipelineTimelines();
  }

  copyToJson(): void {
    const json = JSON.stringify(this.filteredTimelines(), null, 2);
    navigator.clipboard.writeText(json).then(() => {
      console.log('Pipeline timelines copied to clipboard');
    }).catch(err => {
      console.error('Failed to copy to clipboard:', err);
    });
  }

  formatTime(timestamp: string): string {
    const date = new Date(timestamp);
    return date.toLocaleTimeString('en-US', {
      hour12: false,
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      fractionalSecondDigits: 3
    });
  }

  formatStageName(name: string): string {
    // Shorten common stage names
    const shortNames: Record<string, string> = {
      'Parse': 'Parse',
      'Database': 'DB',
      'DbSave': 'DB',
      'Broadcast': 'Broadcast',
      'Projection': 'Project',
      'Webhook': 'Webhook',
      'MqttPublish': 'MQTT',
      'Automation': 'Auto',
      'RuleLookup': 'Rules',
      'ActionExec': 'Action',
      'ZigbeeRoundTrip': '📡 Zigbee'
    };
    return shortNames[name] || name;
  }

  getStageLeft(stage: PipelineStage, timeline: PipelineTimeline): number {
    if (timeline.totalMs === 0) return 0;
    return (stage.startOffsetMs / timeline.totalMs) * 100;
  }

  getStageWidth(stage: PipelineStage, timeline: PipelineTimeline): number {
    if (timeline.totalMs === 0) return 0;
    // Minimum width for visibility
    const minWidthPercent = 2;
    const calculatedWidth = (stage.durationMs / timeline.totalMs) * 100;
    return Math.max(calculatedWidth, minWidthPercent);
  }
}
