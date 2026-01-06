declare module 'drawflow' {
  export interface DrawflowNode {
    id: number;
    name: string;
    data: Record<string, any>;
    class: string;
    html: string;
    typenode: boolean;
    inputs: Record<string, { connections: Array<{ node: string; input: string }> }>;
    outputs: Record<string, { connections: Array<{ node: string; output: string }> }>;
    pos_x: number;
    pos_y: number;
  }

  export interface DrawflowExport {
    drawflow: {
      [moduleName: string]: {
        data: Record<string, DrawflowNode>;
      };
    };
  }

  export default class Drawflow {
    constructor(element: HTMLElement, render?: any, parent?: any);

    reroute: boolean;
    reroute_fix_curvature: boolean;
    force_first_input: boolean;
    editor_mode: string;
    zoom: number;
    zoom_max: number;
    zoom_min: number;

    drawflow: DrawflowExport;

    start(): void;
    clear(): void;

    addNode(
      name: string,
      inputs: number,
      outputs: number,
      pos_x: number,
      pos_y: number,
      className: string,
      data: Record<string, any>,
      html: string,
      typenode?: boolean
    ): number;

    removeNodeId(id: string): void;
    getNodeFromId(id: string | number): DrawflowNode | null;
    updateNodeDataFromId(id: string | number, data: Record<string, any>): void;

    addConnection(
      output_id: string | number,
      input_id: string | number,
      output_class: string,
      input_class: string
    ): void;

    removeSingleConnection(
      output_id: string | number,
      input_id: string | number,
      output_class: string,
      input_class: string
    ): void;

    export(): DrawflowExport;
    import(data: DrawflowExport): void;

    zoom_in(): void;
    zoom_out(): void;
    zoom_reset(): void;

    on(event: string, callback: (...args: any[]) => void): void;

    dispatch(event: string, data: any): void;
  }
}
