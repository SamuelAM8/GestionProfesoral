#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Text.Json;
using ProyectoBackend.Models;
using ProyectoBackend.Services;
using BCrypt.Net;

namespace ProyectoBackend.Controllers
{
    [Route("api/{projectName}/{tableName}")]
    [ApiController]
    [Authorize]
    public class EntidadesController : ControllerBase
    {
        private readonly ControlConexion controlConexion;
        private readonly IConfiguration _configuration;

        public EntidadesController(ControlConexion controlConexion, IConfiguration configuration)
        {
            this.controlConexion = controlConexion ?? throw new ArgumentNullException(nameof(controlConexion));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Listar(string projectName, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
                return BadRequest("El nombre de la tabla no puede estar vacío.");

            try
            {
                var lista = new List<Dictionary<string, object?>>();
                string comandoSQL = $"SELECT * FROM {tableName}";

                controlConexion.AbrirBd();
                var tabla = controlConexion.EjecutarConsultaSql(comandoSQL, null);
                controlConexion.CerrarBd();

                foreach (DataRow fila in tabla.Rows)
                {
                    var propiedades = fila.Table.Columns.Cast<DataColumn>()
                                        .ToDictionary(col => col.ColumnName, col => fila[col] == DBNull.Value ? null : fila[col]);
                    lista.Add(propiedades);
                }

                return Ok(lista);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [AllowAnonymous]
        [HttpGet("{keyName}/{value}")]
        public IActionResult GetByKey(string projectName, string tableName, string keyName, string value)
        {
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(keyName) || string.IsNullOrWhiteSpace(value))
                return BadRequest("El nombre de la tabla, el nombre de la clave y el valor no pueden estar vacíos.");

            controlConexion.AbrirBd();
            try
            {
                string query = "SELECT data_type FROM information_schema.columns WHERE table_name = @tableName AND column_name = @columnName";
                var parameters = new DbParameter[]
                {
                    CreateParameter("@tableName", tableName),
                    CreateParameter("@columnName", keyName)
                };

                var dataTypeResult = controlConexion.EjecutarConsultaSql(query, parameters);

                if (dataTypeResult == null || dataTypeResult.Rows.Count == 0 || dataTypeResult.Rows[0]["data_type"] == DBNull.Value)
                    return NotFound("No se pudo determinar el tipo de dato.");

                string dataType = dataTypeResult.Rows[0]["data_type"]?.ToString() ?? "";

                object convertedValue;
                string comandoSQL;

                switch (dataType.ToLower())
                {
                    case "int":
                    case "bigint":
                    case "integer":
                        if (int.TryParse(value, out int intValue))
                        {
                            convertedValue = intValue;
                            comandoSQL = $"SELECT * FROM {tableName} WHERE {keyName} = @Value";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos entero.");
                        }
                        break;
                    case "decimal":
                    case "numeric":
                        if (decimal.TryParse(value, out decimal decimalValue))
                        {
                            convertedValue = decimalValue;
                            comandoSQL = $"SELECT * FROM {tableName} WHERE {keyName} = @Value";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos número.");
                        }
                        break;
                    case "bit":
                    case "boolean":
                        if (bool.TryParse(value, out bool boolValue))
                        {
                            convertedValue = boolValue;
                            comandoSQL = $"SELECT * FROM {tableName} WHERE {keyName} = @Value";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos booleano.");
                        }
                        break;
                    case "float":
                    case "real":
                        if (double.TryParse(value, out double doubleValue))
                        {
                            convertedValue = doubleValue;
                            comandoSQL = $"SELECT * FROM {tableName} WHERE {keyName} = @Value";
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos doble.");
                        }
                        break;
                    case "nvarchar":
                    case "varchar":
                    case "character varying":
                    case "text":
                    case "char":
                        convertedValue = value;
                        comandoSQL = $"SELECT * FROM {tableName} WHERE {keyName} = @Value";
                        break;
                    case "date":
                    case "datetime":
                        if (DateTime.TryParse(value, out DateTime dateValue))
                        {
                            comandoSQL = $"SELECT * FROM {tableName} WHERE CAST({keyName} AS DATE) = @Value";
                            convertedValue = dateValue.Date;
                        }
                        else
                        {
                            return BadRequest("El valor proporcionado no es válido para el tipo de datos fecha.");
                        }
                        break;
                    default:
                        return BadRequest($"Tipo de dato no soportado: {dataType}");
                }

                var parametro = CreateParameter("@Value", convertedValue);
                var resultado = controlConexion.EjecutarConsultaSql(comandoSQL, new DbParameter[] { parametro });

                if (resultado.Rows.Count > 0)
                {
                    var lista = new List<Dictionary<string, object?>>();
                    foreach (DataRow fila in resultado.Rows)
                    {
                        var propiedades = resultado.Columns.Cast<DataColumn>()
                                           .ToDictionary(col => col.ColumnName, col => fila[col] == DBNull.Value ? null : fila[col]);
                        lista.Add(propiedades);
                    }

                    return Ok(lista);
                }

                return NotFound();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
            finally
            {
                controlConexion.CerrarBd();
            }
        }

        [AllowAnonymous]
        [HttpPost]
        public IActionResult Crear(string projectName, string tableName, [FromBody] Dictionary<string, object?> entidadData)
        {
            if (string.IsNullOrWhiteSpace(tableName) || entidadData == null || !entidadData.Any())
                return BadRequest("El nombre de la tabla y los datos de la entidad no pueden estar vacíos.");

            try
            {
                var propiedades = entidadData.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value is JsonElement jsonElement ? ConvertJsonElement(jsonElement) : kvp.Value);

                // Encriptación de contraseñas si se detectan
                var passwordKeys = new[] { "password", "contrasena", "passw" };
                var passwordKey = propiedades.Keys.FirstOrDefault(k => passwordKeys.Any(pk => k.IndexOf(pk, StringComparison.OrdinalIgnoreCase) >= 0));
                
                if (passwordKey != null)
                {
                    var plainPassword = propiedades[passwordKey]?.ToString();
                    if (!string.IsNullOrEmpty(plainPassword))
                    {
                        propiedades[passwordKey] = BCrypt.Net.BCrypt.HashPassword(plainPassword);
                    }
                }

                var columnas = string.Join(",", propiedades.Keys);
                var valores = string.Join(",", propiedades.Keys.Select(k => $"@{k}"));
                string comandoSQL = $"INSERT INTO {tableName} ({columnas}) VALUES ({valores})";

                var parametros = propiedades.Select(p => CreateParameter($"@{p.Key}", p.Value)).ToArray();

                controlConexion.AbrirBd();
                controlConexion.EjecutarComandoSql(comandoSQL, parametros);
                controlConexion.CerrarBd();

                return Ok("Entidad creada exitosamente.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [AllowAnonymous]
        [HttpPut("{keyName}/{keyValue}")]
        public IActionResult Actualizar(string projectName, string tableName, string keyName, string keyValue, [FromBody] Dictionary<string, object?> entidadData)
        {
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(keyName) || entidadData == null || !entidadData.Any())
                return BadRequest("El nombre de la tabla, el nombre de la clave y los datos de la entidad no pueden estar vacíos.");

            try
            {
                var propiedades = entidadData.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value is JsonElement jsonElement ? ConvertJsonElement(jsonElement) : kvp.Value);

                // Encriptación de contraseñas si se detectan
                var passwordKeys = new[] { "password", "contrasena", "passw" };
                var passwordKey = propiedades.Keys.FirstOrDefault(k => passwordKeys.Any(pk => k.IndexOf(pk, StringComparison.OrdinalIgnoreCase) >= 0));
                
                if (passwordKey != null)
                {
                    var plainPassword = propiedades[passwordKey]?.ToString();
                    if (!string.IsNullOrEmpty(plainPassword))
                    {
                        propiedades[passwordKey] = BCrypt.Net.BCrypt.HashPassword(plainPassword);
                    }
                }

                var actualizaciones = string.Join(",", propiedades.Select(p => $"{p.Key}=@{p.Key}"));
                string comandoSQL = $"UPDATE {tableName} SET {actualizaciones} WHERE {keyName}=@KeyValue";

                var parametros = propiedades.Select(p => CreateParameter($"@{p.Key}", p.Value)).ToList();
                parametros.Add(CreateParameter("@KeyValue", keyValue));

                controlConexion.AbrirBd();
                controlConexion.EjecutarComandoSql(comandoSQL, parametros.ToArray());
                controlConexion.CerrarBd();

                return Ok("Entidad actualizada exitosamente.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [AllowAnonymous]
        [HttpDelete("{keyName}/{keyValue}")]
        public IActionResult Eliminar(string projectName, string tableName, string keyName, string keyValue)
        {
            if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(keyName))
                return BadRequest("El nombre de la tabla o el nombre de la clave no pueden estar vacíos.");

            try
            {
                string comandoSQL = $"DELETE FROM {tableName} WHERE {keyName}=@KeyValue";
                var parametro = CreateParameter("@KeyValue", keyValue);

                controlConexion.AbrirBd();
                controlConexion.EjecutarComandoSql(comandoSQL, new[] { parametro });
                controlConexion.CerrarBd();

                return Ok("Entidad eliminada exitosamente.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        [AllowAnonymous]
        [HttpPost("verificar-contrasena")]
        public IActionResult VerificarContrasena(string projectName, string tableName, [FromBody] Dictionary<string, string> datos)
        {
            if (string.IsNullOrWhiteSpace(tableName) || datos == null || !datos.ContainsKey("userField") || !datos.ContainsKey("passwordField") || !datos.ContainsKey("userValue") || !datos.ContainsKey("passwordValue"))
                return BadRequest("El nombre de la tabla, el campo de usuario, el campo de contraseña, el valor de usuario y el valor de contraseña no pueden estar vacíos.");

            try
            {
                string userField = datos["userField"];
                string passwordField = datos["passwordField"];
                string userValue = datos["userValue"];
                string passwordValue = datos["passwordValue"];

                string comandoSQL = $"SELECT {passwordField} FROM {tableName} WHERE {userField} = @UserValue";
                var parametro = CreateParameter("@UserValue", userValue);

                controlConexion.AbrirBd();
                var resultado = controlConexion.EjecutarConsultaSql(comandoSQL, new DbParameter[] { parametro });
                controlConexion.CerrarBd();

                if (resultado.Rows.Count == 0)
                    return NotFound("Usuario no encontrado.");

                string hashedPassword = resultado.Rows[0][passwordField]?.ToString() ?? string.Empty;

                if (!hashedPassword.StartsWith("$2"))
                    throw new InvalidOperationException("Stored password hash is not a valid BCrypt hash.");

                bool isPasswordValid = BCrypt.Net.BCrypt.Verify(passwordValue, hashedPassword);

                if (isPasswordValid)
                    return Ok("Contraseña verificada exitosamente.");
                else
                    return Unauthorized("Contraseña incorrecta.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error interno del servidor: {ex.Message}");
            }
        }

        public DbParameter CreateParameter(string name, object? value)
        {
            return new SqlParameter(name, value ?? DBNull.Value);
        }

        private object? ConvertJsonElement(JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null)
                return null;

            switch (jsonElement.ValueKind)
            {
                case JsonValueKind.String:
                    return DateTime.TryParse(jsonElement.GetString(), out DateTime dateValue) ? (object)dateValue : jsonElement.GetString();
                case JsonValueKind.Number:
                    return jsonElement.TryGetInt32(out var intValue) ? (object)intValue : jsonElement.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Object:
                    return jsonElement.GetRawText();
                case JsonValueKind.Array:
                    return jsonElement.GetRawText();
                default:
                    throw new InvalidOperationException($"Unsupported JsonValueKind: {jsonElement.ValueKind}");
            }
        }
    }
}
