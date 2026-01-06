import { Component, OnInit, OnDestroy, inject, signal, computed, ElementRef, ViewChild, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DevicesApiService, ZigbeeNetworkMap, ZigbeeDeviceType } from '../../../api/sdhome-client';

// Local interfaces for simulation (to add velocity/force properties)
interface SimNode {
  ieeeAddress: string;
  friendlyName: string;
  networkAddress: number;
  type: ZigbeeDeviceType;
  manufacturer?: string;
  model?: string;
  modelId?: string;
  mainsPowered: boolean;
  linkQuality?: number;
  lastSeen?: Date;
  imageUrl?: string;
  x: number;
  y: number;
  vx: number;
  vy: number;
  fx?: number | null;
  fy?: number | null;
}

interface SimLink {
  sourceIeeeAddress: string;
  targetIeeeAddress: string;
  linkQuality: number;
  depth?: number;
  relationship?: string;
}

@Component({
  selector: 'app-zigbee-network-map',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="network-map-container">
      <div class="network-header">
        <div class="header-left">
          <span class="header-icon">🕸️</span>
          <h2>Zigbee Network Map</h2>
        </div>
        <div class="header-controls">
          <button class="control-btn" (click)="refreshMap()" [disabled]="loading()">
            <span [class.spinning]="loading()">🔄</span>
            {{ loading() ? 'Loading...' : 'Refresh' }}
          </button>
          <button class="control-btn" (click)="resetZoom()">↺ Reset View</button>
        </div>
      </div>

      @if (error()) {
        <div class="error-message">
          <span>⚠️</span> {{ error() }}
        </div>
      }

      <div class="network-canvas-container" #canvasContainer
           (mousedown)="onCanvasMouseDown($event)"
           (mousemove)="onCanvasMouseMove($event)"
           (mouseup)="onCanvasMouseUp($event)"
           (mouseleave)="onCanvasMouseUp($event)"
           (wheel)="onCanvasWheel($event)">
        <svg #svgElement class="network-svg">
          <!-- Main transform group for pan/zoom -->
          <g [attr.transform]="transform()">
            <!-- Links -->
            <g class="links">
              @for (link of simulationLinks(); track link.sourceIeeeAddress + link.targetIeeeAddress) {
                <line
                  [attr.x1]="getNodeX(link.sourceIeeeAddress)"
                  [attr.y1]="getNodeY(link.sourceIeeeAddress)"
                  [attr.x2]="getNodeX(link.targetIeeeAddress)"
                  [attr.y2]="getNodeY(link.targetIeeeAddress)"
                  [attr.stroke]="getLinkColor(link.linkQuality)"
                  [attr.stroke-width]="getLinkWidth(link.linkQuality) / scale()"
                  [attr.stroke-opacity]="0.6"
                  class="link"
                >
                  <title>LQI: {{ link.linkQuality }}</title>
                </line>
              }
            </g>

            <!-- Nodes -->
            <g class="nodes">
              @for (node of simulationNodes(); track node.ieeeAddress) {
                <g class="node-group"
                   [attr.transform]="'translate(' + node.x + ',' + node.y + ')'"
                   (mousedown)="onNodeMouseDown($event, node)"
                   [class.coordinator]="isCoordinator(node)"
                   [class.router]="isRouter(node)"
                   [class.end-device]="isEndDevice(node)"
                   [class.selected]="selectedNode()?.ieeeAddress === node.ieeeAddress">

                  <!-- Node circle -->
                  <circle
                    [attr.r]="getNodeRadius(node)"
                    [attr.fill]="getNodeColor(node)"
                    [attr.stroke]="getNodeStroke(node)"
                    [attr.stroke-width]="2 / scale()"
                    class="node-circle"
                  />

                  <!-- Device image or icon -->
                  @if (node.imageUrl && !isCoordinator(node)) {
                    <clipPath [id]="'clip-' + node.ieeeAddress">
                      <circle [attr.r]="getNodeRadius(node) - 4" />
                    </clipPath>
                    <image
                      [attr.href]="node.imageUrl"
                      [attr.x]="-getNodeRadius(node) + 4"
                      [attr.y]="-getNodeRadius(node) + 4"
                      [attr.width]="(getNodeRadius(node) - 4) * 2"
                      [attr.height]="(getNodeRadius(node) - 4) * 2"
                      [attr.clip-path]="'url(#clip-' + node.ieeeAddress + ')'"
                      (error)="onImageError($event)"
                    />
                  } @else {
                    <text class="node-icon" text-anchor="middle" dominant-baseline="central" [attr.font-size]="16 / scale()">
                      {{ getNodeIcon(node) }}
                    </text>
                  }

                  <!-- Label -->
                  <text
                    class="node-label"
                    [attr.y]="getNodeRadius(node) + 14"
                    text-anchor="middle"
                    [attr.font-size]="10 / scale()">
                    {{ node.friendlyName }}
                  </text>

                  <!-- LQI indicator -->
                  @if (node.linkQuality !== undefined && !isCoordinator(node)) {
                    <g [attr.transform]="'translate(' + (getNodeRadius(node) - 5) + ',' + (-getNodeRadius(node) + 5) + ')'">
                      <circle [attr.r]="8 / scale()" [attr.fill]="getLqiColor(node.linkQuality)" stroke="#1a1a2e" [attr.stroke-width]="1 / scale()"/>
                      <text class="lqi-text" text-anchor="middle" dominant-baseline="central" [attr.font-size]="8 / scale()">
                        {{ node.linkQuality }}
                      </text>
                    </g>
                  }
                </g>
              }
            </g>
          </g>
        </svg>

        <!-- Loading overlay -->
        @if (loading()) {
          <div class="loading-overlay">
            <div class="spinner"></div>
            <span>Scanning Zigbee network...</span>
            <span class="loading-hint">This may take up to 2 minutes on large networks</span>
          </div>
        }
      </div>

      <!-- Legend -->
      <div class="network-legend">
        <div class="legend-item">
          <span class="legend-color coordinator"></span>
          <span>Coordinator</span>
        </div>
        <div class="legend-item">
          <span class="legend-color router"></span>
          <span>Router (mains)</span>
        </div>
        <div class="legend-item">
          <span class="legend-color end-device"></span>
          <span>End Device (battery)</span>
        </div>
        <div class="legend-separator">|</div>
        <div class="legend-item">
          <span class="legend-line good"></span>
          <span>Good LQI (>150)</span>
        </div>
        <div class="legend-item">
          <span class="legend-line medium"></span>
          <span>Medium LQI (50-150)</span>
        </div>
        <div class="legend-item">
          <span class="legend-line poor"></span>
          <span>Poor LQI (&lt;50)</span>
        </div>
      </div>

      <!-- Selected node details -->
      @if (selectedNode()) {
        <div class="node-details">
          <div class="details-header">
            <span class="details-icon">{{ getNodeIcon(selectedNode()!) }}</span>
            <h3>{{ selectedNode()!.friendlyName }}</h3>
            <button class="close-btn" (click)="selectedNode.set(null)">×</button>
          </div>
          <div class="details-content">
            <div class="detail-row">
              <span class="detail-label">Type:</span>
              <span class="detail-value" [class]="getTypeClass(selectedNode()!.type)">{{ getTypeName(selectedNode()!.type) }}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">IEEE Address:</span>
              <span class="detail-value mono">{{ selectedNode()!.ieeeAddress }}</span>
            </div>
            <div class="detail-row">
              <span class="detail-label">Network Address:</span>
              <span class="detail-value mono">{{ selectedNode()!.networkAddress }}</span>
            </div>
            @if (selectedNode()!.manufacturer) {
              <div class="detail-row">
                <span class="detail-label">Manufacturer:</span>
                <span class="detail-value">{{ selectedNode()!.manufacturer }}</span>
              </div>
            }
            @if (selectedNode()!.model) {
              <div class="detail-row">
                <span class="detail-label">Model:</span>
                <span class="detail-value">{{ selectedNode()!.model }}</span>
              </div>
            }
            @if (selectedNode()!.linkQuality !== undefined) {
              <div class="detail-row">
                <span class="detail-label">Link Quality:</span>
                <span class="detail-value" [style.color]="getLqiColor(selectedNode()!.linkQuality!)">
                  {{ selectedNode()!.linkQuality }} / 255
                </span>
              </div>
            }
            <div class="detail-row">
              <span class="detail-label">Power:</span>
              <span class="detail-value">{{ selectedNode()!.mainsPowered ? '🔌 Mains' : '🔋 Battery' }}</span>
            </div>
            @if (selectedNode()!.lastSeen) {
              <div class="detail-row">
                <span class="detail-label">Last Seen:</span>
                <span class="detail-value">{{ formatLastSeen(selectedNode()!.lastSeen!) }}</span>
              </div>
            }
            <div class="detail-row">
              <span class="detail-label">Connections:</span>
              <span class="detail-value">{{ getConnectionCount(selectedNode()!) }}</span>
            </div>
          </div>
        </div>
      }

      <!-- Stats bar -->
      <div class="stats-bar">
        <div class="stat">
          <span class="stat-value">{{ coordinatorCount() }}</span>
          <span class="stat-label">Coordinator</span>
        </div>
        <div class="stat">
          <span class="stat-value">{{ routerCount() }}</span>
          <span class="stat-label">Routers</span>
        </div>
        <div class="stat">
          <span class="stat-value">{{ endDeviceCount() }}</span>
          <span class="stat-label">End Devices</span>
        </div>
        <div class="stat">
          <span class="stat-value">{{ linkCount() }}</span>
          <span class="stat-label">Links</span>
        </div>
        @if (networkMap()) {
          <div class="stat generated-at">
            <span class="stat-label">Generated:</span>
            <span class="stat-value">{{ formatDate(networkMap()!.generatedAt) }}</span>
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    .network-map-container {
      display: flex;
      flex-direction: column;
      height: 100%;
      background: var(--bg-primary, #0d1117);
      color: var(--text-primary, #c9d1d9);
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif;
    }

    .network-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 20px;
      background: var(--bg-secondary, #161b22);
      border-bottom: 1px solid var(--border-primary, #30363d);
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 12px;
    }

    .header-icon {
      font-size: 24px;
    }

    .header-left h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
    }

    .header-controls {
      display: flex;
      gap: 8px;
    }

    .control-btn {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 16px;
      background: var(--bg-tertiary, #21262d);
      border: 1px solid var(--border-primary, #30363d);
      border-radius: 6px;
      color: var(--text-primary, #c9d1d9);
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s ease;
    }

    .control-btn:hover:not(:disabled) {
      background: var(--bg-secondary, #30363d);
      border-color: var(--border-secondary, #484f58);
    }

    .control-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .spinning {
      display: inline-block;
      animation: spin 1s linear infinite;
    }

    @keyframes spin {
      from { transform: rotate(0deg); }
      to { transform: rotate(360deg); }
    }

    .error-message {
      padding: 12px 20px;
      background: rgba(248, 81, 73, 0.1);
      border-bottom: 1px solid rgba(248, 81, 73, 0.4);
      color: #f85149;
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .network-canvas-container {
      flex: 1;
      position: relative;
      overflow: hidden;
      background: radial-gradient(ellipse at center, #1a1a2e 0%, #0d1117 100%);
    }

    .network-svg {
      width: 100%;
      height: 100%;
      cursor: grab;
    }

    .network-svg:active {
      cursor: grabbing;
    }

    .link {
      stroke-linecap: round;
    }

    .node-group {
      cursor: pointer;
      transition: transform 0.1s ease;
    }

    .node-group:hover .node-circle {
      filter: brightness(1.2);
    }

    .node-group.selected .node-circle {
      filter: drop-shadow(0 0 8px currentColor);
    }

    .node-circle {
      transition: all 0.2s ease;
    }

    .node-icon {
      fill: white;
      pointer-events: none;
    }

    .node-label {
      fill: var(--text-secondary, #8b949e);
      font-size: 10px;
      font-weight: 500;
      pointer-events: none;
    }

    .lqi-text {
      fill: white;
      font-weight: bold;
    }

    .loading-overlay {
      position: absolute;
      inset: 0;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 16px;
      background: rgba(13, 17, 23, 0.8);
      backdrop-filter: blur(4px);
    }

    .spinner {
      width: 40px;
      height: 40px;
      border: 3px solid var(--border-primary, #30363d);
      border-top-color: var(--accent-primary, #238636);
      border-radius: 50%;
      animation: spin 1s linear infinite;
    }

    .loading-hint {
      font-size: 12px;
      color: var(--text-secondary, #8b949e);
      margin-top: -8px;
    }

    .network-legend {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 10px 20px;
      background: var(--bg-secondary, #161b22);
      border-top: 1px solid var(--border-primary, #30363d);
      font-size: 12px;
    }

    .legend-item {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .legend-color {
      width: 12px;
      height: 12px;
      border-radius: 50%;
    }

    .legend-color.coordinator {
      background: #ffd700;
      box-shadow: 0 0 6px #ffd700;
    }

    .legend-color.router {
      background: #22c55e;
    }

    .legend-color.end-device {
      background: #3b82f6;
    }

    .legend-separator {
      color: var(--border-primary, #30363d);
    }

    .legend-line {
      width: 24px;
      height: 3px;
      border-radius: 2px;
    }

    .legend-line.good {
      background: #22c55e;
    }

    .legend-line.medium {
      background: #fbbf24;
    }

    .legend-line.poor {
      background: #ef4444;
    }

    .node-details {
      position: absolute;
      top: 20px;
      right: 20px;
      width: 280px;
      background: var(--bg-secondary, #161b22);
      border: 1px solid var(--border-primary, #30363d);
      border-radius: 8px;
      overflow: hidden;
      box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
    }

    .details-header {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 16px;
      background: var(--bg-tertiary, #21262d);
      border-bottom: 1px solid var(--border-primary, #30363d);
    }

    .details-icon {
      font-size: 20px;
    }

    .details-header h3 {
      flex: 1;
      margin: 0;
      font-size: 14px;
      font-weight: 600;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .close-btn {
      background: none;
      border: none;
      color: var(--text-secondary, #8b949e);
      font-size: 20px;
      cursor: pointer;
      padding: 0;
      line-height: 1;
    }

    .close-btn:hover {
      color: var(--text-primary, #c9d1d9);
    }

    .details-content {
      padding: 12px 16px;
    }

    .detail-row {
      display: flex;
      justify-content: space-between;
      padding: 6px 0;
      border-bottom: 1px solid var(--border-primary, #30363d);
    }

    .detail-row:last-child {
      border-bottom: none;
    }

    .detail-label {
      color: var(--text-secondary, #8b949e);
      font-size: 12px;
    }

    .detail-value {
      font-size: 12px;
      font-weight: 500;
    }

    .detail-value.mono {
      font-family: 'SF Mono', Monaco, monospace;
      font-size: 11px;
    }

    .detail-value.coordinator {
      color: #ffd700;
    }

    .detail-value.router {
      color: #22c55e;
    }

    .detail-value.enddevice {
      color: #3b82f6;
    }

    .stats-bar {
      display: flex;
      gap: 24px;
      padding: 12px 20px;
      background: var(--bg-tertiary, #21262d);
      border-top: 1px solid var(--border-primary, #30363d);
    }

    .stat {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 2px;
    }

    .stat-value {
      font-size: 18px;
      font-weight: 600;
      color: var(--text-primary, #c9d1d9);
    }

    .stat-label {
      font-size: 11px;
      color: var(--text-secondary, #8b949e);
    }

    .stat.generated-at {
      margin-left: auto;
      flex-direction: row;
      gap: 8px;
      align-items: baseline;
    }

    .stat.generated-at .stat-value {
      font-size: 12px;
      font-weight: 400;
    }
  `]
})
export class ZigbeeNetworkMapComponent implements OnInit, OnDestroy, AfterViewInit {
  private apiService = inject(DevicesApiService);

  @ViewChild('svgElement') svgElement!: ElementRef<SVGSVGElement>;
  @ViewChild('canvasContainer') canvasContainer!: ElementRef<HTMLDivElement>;

  networkMap = signal<ZigbeeNetworkMap | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  selectedNode = signal<SimNode | null>(null);

  // Simulation state
  simulationNodes = signal<SimNode[]>([]);
  simulationLinks = signal<SimLink[]>([]);

  // Pan/zoom state - using simple transform approach
  private panX = 0;
  private panY = 0;
  scale = signal(1);
  transform = signal('translate(0, 0) scale(1)');

  // Interaction state
  private isPanning = false;
  private lastMouseX = 0;
  private lastMouseY = 0;
  private draggedNode: SimNode | null = null;

  // Simulation
  private animationFrameId: number | null = null;
  private simulationRunning = false;

  coordinatorCount = computed(() => this.simulationNodes().filter(n => n.type === ZigbeeDeviceType.Coordinator).length);
  routerCount = computed(() => this.simulationNodes().filter(n => n.type === ZigbeeDeviceType.Router).length);
  endDeviceCount = computed(() => this.simulationNodes().filter(n => n.type === ZigbeeDeviceType.EndDevice).length);
  linkCount = computed(() => this.simulationLinks().length);

  ngOnInit() {
    this.refreshMap();
  }

  ngAfterViewInit() {
    this.resetZoom();
  }

  ngOnDestroy() {
    this.stopSimulation();
  }

  async refreshMap() {
    this.loading.set(true);
    this.error.set(null);

    try {
      const map = await this.apiService.getNetworkMap().toPromise();
      if (map) {
        this.networkMap.set(map);
        this.initializeSimulation(map);
      }
    } catch (err: any) {
      console.error('Failed to load network map:', err);
      this.error.set(err.error?.error || err.message || 'Failed to load network map');
    } finally {
      this.loading.set(false);
    }
  }

  private initializeSimulation(map: ZigbeeNetworkMap) {
    const apiNodes = map.nodes || [];

    // Initialize node positions centered around 0,0
    const nodes: SimNode[] = apiNodes.map((node, index) => {
      let x: number, y: number;

      if (node.type === ZigbeeDeviceType.Coordinator) {
        // Coordinator at center (0,0)
        x = 0;
        y = 0;
      } else if (node.type === ZigbeeDeviceType.Router) {
        // Routers in a ring around coordinator
        const routerIndex = apiNodes.filter((n, i) => n.type === ZigbeeDeviceType.Router && i < index).length;
        const routerCount = apiNodes.filter(n => n.type === ZigbeeDeviceType.Router).length;
        const angle = (2 * Math.PI * routerIndex) / (routerCount || 1);
        const radius = 150;
        x = radius * Math.cos(angle);
        y = radius * Math.sin(angle);
      } else {
        // End devices further out
        const angle = Math.random() * 2 * Math.PI;
        const radius = 200 + Math.random() * 100;
        x = radius * Math.cos(angle);
        y = radius * Math.sin(angle);
      }

      return {
        ieeeAddress: node.ieeeAddress || '',
        friendlyName: node.friendlyName || '',
        networkAddress: node.networkAddress || 0,
        type: node.type ?? ZigbeeDeviceType.EndDevice,
        manufacturer: node.manufacturer,
        model: node.model,
        modelId: node.modelId,
        mainsPowered: node.mainsPowered ?? false,
        linkQuality: node.linkQuality,
        lastSeen: node.lastSeen,
        imageUrl: node.imageUrl,
        x,
        y,
        vx: 0,
        vy: 0
      };
    });

    const apiLinks = map.links || [];
    const links: SimLink[] = apiLinks.map(link => ({
      sourceIeeeAddress: link.sourceIeeeAddress || '',
      targetIeeeAddress: link.targetIeeeAddress || '',
      linkQuality: link.linkQuality || 0,
      depth: link.depth,
      relationship: link.relationship
    }));

    this.simulationNodes.set(nodes);
    this.simulationLinks.set(links);

    // Initialize transform to center on the canvas
    const width = this.canvasContainer?.nativeElement?.clientWidth || 800;
    const height = this.canvasContainer?.nativeElement?.clientHeight || 600;
    this.panX = width / 2;
    this.panY = height / 2;
    this.scale.set(1);
    this.updateTransform();

    // Start physics simulation
    this.startSimulation();
  }

  private startSimulation() {
    this.simulationRunning = true;
    this.runSimulation();
  }

  private stopSimulation() {
    this.simulationRunning = false;
    if (this.animationFrameId) {
      cancelAnimationFrame(this.animationFrameId);
      this.animationFrameId = null;
    }
  }

  private runSimulation() {
    if (!this.simulationRunning) return;

    const nodes = [...this.simulationNodes()];
    const links = this.simulationLinks();

    // Apply forces
    this.applyForces(nodes, links);

    // Update positions
    for (const node of nodes) {
      if (node.fx !== null && node.fx !== undefined) {
        node.x = node.fx;
        node.vx = 0;
      } else {
        node.vx *= 0.9; // Damping
        node.x += node.vx;
      }

      if (node.fy !== null && node.fy !== undefined) {
        node.y = node.fy;
        node.vy = 0;
      } else {
        node.vy *= 0.9; // Damping
        node.y += node.vy;
      }
    }

    this.simulationNodes.set(nodes);

    // Continue simulation if there's still movement
    const totalVelocity = nodes.reduce((sum, n) => sum + Math.abs(n.vx) + Math.abs(n.vy), 0);
    if (totalVelocity > 0.1) {
      this.animationFrameId = requestAnimationFrame(() => this.runSimulation());
    } else {
      this.simulationRunning = false;
    }
  }

  private applyForces(nodes: SimNode[], links: SimLink[]) {
    // Repulsion between all nodes
    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const dx = nodes[j].x - nodes[i].x;
        const dy = nodes[j].y - nodes[i].y;
        const dist = Math.sqrt(dx * dx + dy * dy) || 1;
        const force = 1000 / (dist * dist);

        const fx = (dx / dist) * force;
        const fy = (dy / dist) * force;

        nodes[i].vx -= fx;
        nodes[i].vy -= fy;
        nodes[j].vx += fx;
        nodes[j].vy += fy;
      }
    }

    // Attraction along links
    for (const link of links) {
      const source = nodes.find(n => n.ieeeAddress === link.sourceIeeeAddress);
      const target = nodes.find(n => n.ieeeAddress === link.targetIeeeAddress);

      if (source && target) {
        const dx = target.x - source.x;
        const dy = target.y - source.y;
        const dist = Math.sqrt(dx * dx + dy * dy) || 1;
        const targetDist = 100;
        const force = (dist - targetDist) * 0.01;

        const fx = (dx / dist) * force;
        const fy = (dy / dist) * force;

        source.vx += fx;
        source.vy += fy;
        target.vx -= fx;
        target.vy -= fy;
      }
    }

    // Center gravity (pull toward 0,0)
    for (const node of nodes) {
      node.vx -= node.x * 0.001;
      node.vy -= node.y * 0.001;
    }

    // Keep coordinator fixed at center (0,0)
    const coordinator = nodes.find(n => n.type === ZigbeeDeviceType.Coordinator);
    if (coordinator) {
      coordinator.fx = 0;
      coordinator.fy = 0;
    }
  }

  getNodeX(ieeeAddress: string): number {
    const node = this.simulationNodes().find(n => n.ieeeAddress === ieeeAddress);
    return node?.x || 0;
  }

  getNodeY(ieeeAddress: string): number {
    const node = this.simulationNodes().find(n => n.ieeeAddress === ieeeAddress);
    return node?.y || 0;
  }

  getNodeRadius(node: SimNode): number {
    switch (node.type) {
      case ZigbeeDeviceType.Coordinator: return 30;
      case ZigbeeDeviceType.Router: return 22;
      default: return 18;
    }
  }

  getNodeColor(node: SimNode): string {
    switch (node.type) {
      case ZigbeeDeviceType.Coordinator: return '#ffd700';
      case ZigbeeDeviceType.Router: return '#22c55e';
      default: return '#3b82f6';
    }
  }

  getNodeStroke(node: SimNode): string {
    switch (node.type) {
      case ZigbeeDeviceType.Coordinator: return '#ffed4a';
      case ZigbeeDeviceType.Router: return '#4ade80';
      default: return '#60a5fa';
    }
  }

  getNodeIcon(node: SimNode): string {
    if (node.type === ZigbeeDeviceType.Coordinator) return '📡';
    if (node.mainsPowered) return '🔌';
    return '🔋';
  }

  getTypeName(type: ZigbeeDeviceType): string {
    switch (type) {
      case ZigbeeDeviceType.Coordinator: return 'Coordinator';
      case ZigbeeDeviceType.Router: return 'Router';
      case ZigbeeDeviceType.EndDevice: return 'EndDevice';
      default: return 'Unknown';
    }
  }

  getTypeClass(type: ZigbeeDeviceType): string {
    switch (type) {
      case ZigbeeDeviceType.Coordinator: return 'coordinator';
      case ZigbeeDeviceType.Router: return 'router';
      case ZigbeeDeviceType.EndDevice: return 'enddevice';
      default: return '';
    }
  }

  isCoordinator(node: SimNode): boolean {
    return node.type === ZigbeeDeviceType.Coordinator;
  }

  isRouter(node: SimNode): boolean {
    return node.type === ZigbeeDeviceType.Router;
  }

  isEndDevice(node: SimNode): boolean {
    return node.type === ZigbeeDeviceType.EndDevice;
  }

  getLinkColor(lqi: number): string {
    if (lqi >= 150) return '#22c55e';
    if (lqi >= 50) return '#fbbf24';
    return '#ef4444';
  }

  getLinkWidth(lqi: number): number {
    return Math.max(1, Math.min(4, lqi / 60));
  }

  getLqiColor(lqi: number): string {
    if (lqi >= 200) return '#22c55e';
    if (lqi >= 150) return '#84cc16';
    if (lqi >= 100) return '#fbbf24';
    if (lqi >= 50) return '#f97316';
    return '#ef4444';
  }

  getConnectionCount(node: SimNode): number {
    const links = this.simulationLinks();
    return links.filter(l =>
      l.sourceIeeeAddress === node.ieeeAddress ||
      l.targetIeeeAddress === node.ieeeAddress
    ).length;
  }

  formatLastSeen(date: Date): string {
    const now = new Date();
    const diff = now.getTime() - date.getTime();

    if (diff < 60000) return 'Just now';
    if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
    if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
    return date.toLocaleDateString();
  }

  formatDate(date: Date | undefined): string {
    if (!date) return '';
    return date.toLocaleTimeString();
  }

  onImageError(event: Event) {
    const img = event.target as SVGImageElement;
    img.style.display = 'none';
  }

  // ============================================
  // Pan/Zoom handlers - Clean implementation
  // ============================================

  onCanvasMouseDown(event: MouseEvent) {
    // Only start panning if clicking on the canvas background (not a node)
    const target = event.target as Element;
    if (target.tagName === 'svg' || target.classList.contains('link') || target.closest('.network-canvas-container') === event.currentTarget) {
      if (target.closest('.node-group')) return; // Don't pan when clicking on nodes

      this.isPanning = true;
      this.lastMouseX = event.clientX;
      this.lastMouseY = event.clientY;
      this.selectedNode.set(null);
      event.preventDefault();
    }
  }

  onCanvasMouseMove(event: MouseEvent) {
    if (this.draggedNode) {
      // Dragging a node
      const rect = this.canvasContainer.nativeElement.getBoundingClientRect();
      const currentScale = this.scale();

      // Convert screen coordinates to SVG coordinates
      const svgX = (event.clientX - rect.left - this.panX) / currentScale;
      const svgY = (event.clientY - rect.top - this.panY) / currentScale;

      this.draggedNode.fx = svgX;
      this.draggedNode.fy = svgY;
      this.draggedNode.x = svgX;
      this.draggedNode.y = svgY;

      this.simulationNodes.set([...this.simulationNodes()]);
    } else if (this.isPanning) {
      // Panning the canvas
      const deltaX = event.clientX - this.lastMouseX;
      const deltaY = event.clientY - this.lastMouseY;

      this.panX += deltaX;
      this.panY += deltaY;

      this.lastMouseX = event.clientX;
      this.lastMouseY = event.clientY;

      this.updateTransform();
    }
  }

  onCanvasMouseUp(event: MouseEvent) {
    if (this.draggedNode) {
      this.draggedNode.fx = null;
      this.draggedNode.fy = null;
      this.draggedNode = null;
      this.startSimulation();
    }
    this.isPanning = false;
  }

  onNodeMouseDown(event: MouseEvent, node: SimNode) {
    event.stopPropagation();
    this.selectedNode.set(node);
    this.draggedNode = node;
    node.fx = node.x;
    node.fy = node.y;
  }

  onCanvasWheel(event: WheelEvent) {
    event.preventDefault();

    const rect = this.canvasContainer.nativeElement.getBoundingClientRect();
    const mouseX = event.clientX - rect.left;
    const mouseY = event.clientY - rect.top;

    // Determine zoom direction with smooth factor
    const zoomFactor = event.deltaY < 0 ? 1.08 : 0.92;
    const oldScale = this.scale();
    let newScale = oldScale * zoomFactor;

    // Clamp scale between 0.2 and 4
    newScale = Math.max(0.2, Math.min(4, newScale));

    // Zoom toward mouse position
    // The idea: keep the point under the mouse stationary
    const scaleRatio = newScale / oldScale;
    this.panX = mouseX - (mouseX - this.panX) * scaleRatio;
    this.panY = mouseY - (mouseY - this.panY) * scaleRatio;

    this.scale.set(newScale);
    this.updateTransform();
  }

  private updateTransform() {
    this.transform.set(`translate(${this.panX}, ${this.panY}) scale(${this.scale()})`);
  }

  resetZoom() {
    // Reset to center the content
    const width = this.canvasContainer?.nativeElement?.clientWidth || 800;
    const height = this.canvasContainer?.nativeElement?.clientHeight || 600;

    this.panX = width / 2;
    this.panY = height / 2;
    this.scale.set(1);
    this.updateTransform();

    // Also re-center the simulation
    this.centerSimulation();
  }

  private centerSimulation() {
    const nodes = this.simulationNodes();
    if (nodes.length === 0) return;

    // Calculate bounds
    let minX = Infinity, maxX = -Infinity;
    let minY = Infinity, maxY = -Infinity;

    for (const node of nodes) {
      minX = Math.min(minX, node.x);
      maxX = Math.max(maxX, node.x);
      minY = Math.min(minY, node.y);
      maxY = Math.max(maxY, node.y);
    }

    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;

    // Move all nodes so the center is at 0,0
    const updatedNodes = nodes.map(n => ({
      ...n,
      x: n.x - centerX,
      y: n.y - centerY
    }));

    this.simulationNodes.set(updatedNodes);
  }
}
