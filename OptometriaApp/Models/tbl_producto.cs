using System;
using System.Collections.Generic;

namespace OptometriaApp.Models;

public partial class tbl_producto
{
    public int id_producto { get; set; }

    public int? id_proveedor { get; set; }

    public int? id_categoria { get; set; }

    public string codigo_producto { get; set; } = null!;

    public string nombre_producto { get; set; } = null!;

    public string? tipo_item { get; set; }

    public string? descripcion { get; set; }

    public string? imagen_url { get; set; }

    public decimal? precio_costo { get; set; }

    public decimal precio_venta { get; set; }

    public bool? tiene_iva { get; set; }

    public decimal? porcentaje_iva { get; set; }

    public int? stock_actual { get; set; }

    public int? stock_minimo { get; set; }

    public bool? activo { get; set; }

    public string? almacen { get; set; }

    public string? pasillo { get; set; }

    public string? estante { get; set; }

    public string? nivel { get; set; }

    public int? stock_maximo { get; set; }

    public int? punto_reorden { get; set; }

    public int? cantidad_empaque { get; set; }

    public decimal? peso_unitario { get; set; }

    public decimal? dimensiones_largo { get; set; }

    public decimal? dimensiones_ancho { get; set; }

    public decimal? dimensiones_alto { get; set; }

    public decimal? volumen_m3 { get; set; }

    public bool? requiere_lote { get; set; }

    public bool? requiere_fecha_vencimiento { get; set; }

    public int? dias_vencimiento { get; set; }

    public string? cuenta_contable { get; set; }

    public string? centro_costo { get; set; }

    public string? naturaleza_item { get; set; }

    public decimal? porcentaje_margen { get; set; }

    public decimal? descuento_mayorista { get; set; }

    public decimal? descuento_cliente_fijo { get; set; }

    public string? movimiento_frecuencia { get; set; }

    public int? dias_rotacion_promedio { get; set; }

    public DateTime? fecha_ultima_compra { get; set; }

    public DateTime? fecha_ultima_venta { get; set; }

    public DateTime? fecha_actualizacion_precio { get; set; }

    public int? id_usuario_actualizacion { get; set; }

    public int? cantidad_movimientos_mes { get; set; }

    public string? etiquetas { get; set; }

    public string? marca { get; set; }

    public string? modelo { get; set; }

    public string? color { get; set; }

    public string? talla { get; set; }

    public bool? es_promocion { get; set; }

    public decimal? porcentaje_descuento_promo { get; set; }

    public string? material_principal { get; set; }

    public string? tratamiento_lente { get; set; }

    public string? estado_producto { get; set; }

    public string? motivo_estado { get; set; }

    public string? codigo_barras { get; set; }

    public string? sku_alterno { get; set; }

    public string? referencia_fabricante { get; set; }

    public string? proveedor_preferente { get; set; }

    public int? tiempo_entrega_dias { get; set; }

    public int? cantidad_pedido_optima { get; set; }

    public DateTime? fecha_creacion { get; set; }

    public string? usuario_creacion { get; set; }

    public string? notas_internas { get; set; }

    public virtual tbl_categoria_producto? id_categoriaNavigation { get; set; }

    public virtual tbl_proveedor? id_proveedorNavigation { get; set; }

    public virtual tbl_usuario? id_usuario_actualizacionNavigation { get; set; }

    public virtual ICollection<tbl_detalle_venta> tbl_detalle_venta { get; set; } = new List<tbl_detalle_venta>();

    public virtual ICollection<tbl_movimiento_inventario> tbl_movimiento_inventarios { get; set; } = new List<tbl_movimiento_inventario>();

    public virtual ICollection<tbl_lote_producto> tbl_lote_producto { get; set; } = new List<tbl_lote_producto>();

    public virtual ICollection<tbl_detalle_orden_compra> tbl_detalle_orden_compra { get; set; } = new List<tbl_detalle_orden_compra>();

    public virtual ICollection<tbl_kardex> tbl_kardex { get; set; } = new List<tbl_kardex>();

    public virtual ICollection<tbl_receta_medica_detalle> tbl_receta_medica_detalle { get; set; } = new List<tbl_receta_medica_detalle>();

    public virtual ICollection<tbl_cobro_transferencia_detalle> tbl_cobro_transferencia_detalles { get; set; } = new List<tbl_cobro_transferencia_detalle>();
}
