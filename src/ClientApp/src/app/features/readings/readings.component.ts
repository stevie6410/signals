import { Component, OnInit, OnDestroy, inject, signal, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, debounceTime } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReadingsApiService, SensorReading } from '../../api/sdhome-client';
import { SignalRService } from '../../core/services/signalr.service';
import { CapabilityMappingService } from '../../core/services/capability-mapping.service';

interface FilterOption {
  label: string;
  value: string;
}

interface MetricSummary {
  metric: string;
  latest: number;
  previousValue: number | null;
  unit: string;
  icon: string;
  trend: 'up' | 'down' | 'stable';
  lastUpdated: Date;
}

interface MetricData {
  reading: SensorReading;
  previousValue: number | null;
  trend: 'up' | 'down' | 'stable';
}

@Component({
  selector: 'app-readings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './readings.component.html',
  styleUrl: './readings.component.scss'
})
export class ReadingsComponent implements OnInit, OnDestroy {
  private readingsService = inject(ReadingsApiService);
  private signalrService = inject(SignalRService);
  private capabilityMapping = inject(CapabilityMappingService);

  // Debounce configuration
  private readonly DEBOUNCE_MS = 300;
  private metricUpdateSubject = new Subject<Map<string, MetricData>>();

  // State
  readings = signal<SensorReading[]>([]);
  loading = signal(false);
  searchFilter = signal('');
  metricFilter = signal<string | null>(null);
  selectedMetric = signal<string | null>(null);
  showMetricDropdown = false;

  // Live readings from SignalR
  liveReadings = this.signalrService.readingHistory;
  isConnected = this.signalrService.isConnected;

  // Combined readings
  allReadings = computed(() => {
    const live = this.liveReadings();
    const historical = this.readings();
    const merged = [...live, ...historical];
    const seen = new Set<string>();
    return merged.filter(r => {
      if (!r.id || seen.has(r.id)) return false;
      seen.add(r.id);
      return true;
    }).slice(0, 500);
  });

  // Filtered readings
  filteredReadings = computed(() => {
    let data = this.allReadings();
    const search = this.searchFilter().toLowerCase();
    const metric = this.metricFilter();

    if (search) {
      data = data.filter(r =>
        r.deviceId?.toLowerCase().includes(search) ||
        r.metric?.toLowerCase().includes(search)
      );
    }

    if (metric) {
      data = data.filter(r => r.metric === metric);
    }

    return data;
  });

  // Unique metrics for filter
  uniqueMetrics = computed<FilterOption[]>(() => {
    const metrics = new Set(this.allReadings().map(r => r.metric).filter(Boolean));
    return [
      { label: 'All Metrics', value: '' },
      ...Array.from(metrics).map(m => ({ label: m!, value: m! }))
    ];
  });

  // Track previous values for trend detection (plain object, not a signal)
  private previousValues = new Map<string, number>();

  // Known metrics in stable order (plain array, updated via effect)
  private knownMetricsList: string[] = [];

  // Processed metric data with trends (signal updated by effect)
  private metricDataMap = signal<Map<string, MetricData>>(new Map());

  // Raw latest readings by metric (pure computed, no side effects)
  private latestByMetric = computed(() => {
    const data = this.allReadings();
    const latest = new Map<string, SensorReading>();

    data.forEach(r => {
      if (!r.metric) return;
      const existing = latest.get(r.metric);
      if (!existing || new Date(r.timestampUtc!) > new Date(existing.timestampUtc!)) {
        latest.set(r.metric, r);
      }
    });

    return latest;
  });

  // Effect to update metric data and track trends (side effects allowed here)
  private updateMetricDataEffect = effect(() => {
    const latestByMetric = this.latestByMetric();
    const newMetricData = new Map<string, MetricData>();

    // Update known metrics list (preserve order, add new ones)
    const currentMetrics = Array.from(latestByMetric.keys());
    const newMetrics = currentMetrics.filter(m => !this.knownMetricsList.includes(m));
    if (newMetrics.length > 0) {
      this.knownMetricsList = [...this.knownMetricsList, ...newMetrics];
    }

    // Build metric data with trends
    for (const [metric, reading] of latestByMetric) {
      const currentValue = reading.value ?? 0;
      const previousValue = this.previousValues.get(metric) ?? null;

      // Determine trend
      let trend: 'up' | 'down' | 'stable' = 'stable';
      if (previousValue !== null && previousValue !== currentValue) {
        trend = currentValue > previousValue ? 'up' : 'down';
      }

      // Update previous value for next comparison
      this.previousValues.set(metric, currentValue);

      newMetricData.set(metric, { reading, previousValue, trend });
    }

    // Push to debounce subject instead of directly updating
    this.metricUpdateSubject.next(newMetricData);
  });

  // Subscribe to debounced updates
  private metricUpdateSubscription = this.metricUpdateSubject.pipe(
    debounceTime(this.DEBOUNCE_MS)
  ).subscribe(data => this.metricDataMap.set(data));

  // Metric summaries (pure computed, reads from metricDataMap)
  metricSummaries = computed<MetricSummary[]>(() => {
    const metricData = this.metricDataMap();

    return this.knownMetricsList
      .filter(metric => metricData.has(metric))
      .map(metric => {
        const data = metricData.get(metric)!;
        const reading = data.reading;

        return {
          metric,
          latest: reading.value ?? 0,
          previousValue: data.previousValue,
          unit: reading.unit ?? '',
          icon: this.getMetricIcon(metric),
          trend: data.trend,
          lastUpdated: new Date(reading.timestampUtc!)
        };
      });
  });

  ngOnInit() {
    this.capabilityMapping.ensureLoaded();
    this.loadReadings();
  }

  ngOnDestroy() {
    this.metricUpdateSubject.complete();
    this.metricUpdateSubscription.unsubscribe();
  }

  loadReadings() {
    this.loading.set(true);
    this.readingsService.getRecentReadings(200).subscribe({
      next: (data) => {
        this.readings.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading readings:', err);
        this.loading.set(false);
      }
    });
  }

  refresh() {
    this.loadReadings();
  }

  selectMetric(value: string) {
    this.metricFilter.set(value || null);
    this.showMetricDropdown = false;
  }

  getSelectedMetricLabel(): string {
    const metric = this.metricFilter();
    if (!metric) return 'All Metrics';
    return metric;
  }

  selectMetricCard(metric: string) {
    this.selectedMetric.set(metric);
    this.metricFilter.set(metric);
  }

  getMetricIcon(metric: string): string {
    // Use capability mapping if available
    const display = this.capabilityMapping.getCapabilityDisplay(metric);
    return display.icon;
  }

  getMetricDisplayName(metric: string): string {
    const display = this.capabilityMapping.getCapabilityDisplay(metric);
    return display.displayName;
  }

  getMetricUnit(metric: string): string | undefined {
    const display = this.capabilityMapping.getCapabilityDisplay(metric);
    return display.unit;
  }

  formatValue(value: number | undefined, unit: string | undefined): string {
    if (value === undefined) return '-';
    const formatted = Number.isInteger(value) ? value.toString() : value.toFixed(1);
    return unit ? `${formatted} ${unit}` : formatted;
  }

  formatTimestamp(date: Date | string | undefined): string {
    if (!date) return '-';
    const d = new Date(date);
    return d.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false
    });
  }

  trackReading(index: number, reading: SensorReading): string {
    return reading.id ?? index.toString();
  }
}
