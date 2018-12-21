using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Text;
namespace SqlCodeFactory
{
    /// <summary>
    /// 将数据库文件生成以schema.database为命名空间 table 为类名的工具类
    /// </summary>
    public class SqlCodeUtility
    {
        private const string namespaceFormat = "namespace {0}.{1}";
        private const string classFormat = "\tpublic class {0}";
        private const string fieldFormat = "\t\tprivate {0} _{1};";
        private const string propertyFormat = "\t\tpublic {0} {1}";
        private const string getFormat = "\t\t\tget {{ return _{0}; }}";
        private const string setFormat = "\t\t\tset {{ _{0} = value; }}";
        private string codePath;
        private SqlConnection sqlCon;
        /// <summary>
        /// SqlCodeUtility结构方法
        /// </summary>
        /// <param name="path">生成代码的路径</param>
        public SqlCodeUtility(string path)
        {
            codePath = path;
        }
        /// <summary>
        /// 根据数据库名生成所有表的类
        /// </summary>
        /// <param name="dataSource">数据库地址</param>
        /// <param name="initialCatalog">数据库名称</param>
        /// <param name="userId">登陆id</param>
        /// <param name="password">登陆密码</param>
        public void CreateSqlEFClassByConParams(string dataSource,string initialCatalog,string userId,string password)
        {
            string conFormat = "Data Source={0};Initial Catalog={1};User Id = {2};Password = {3};";
            string conStr = String.Format(conFormat, dataSource, initialCatalog, userId, password);
            Dictionary<string, List<INFORMATION_SCHEMA_COLUMNS>> tableInfo = new Dictionary<string, List<INFORMATION_SCHEMA_COLUMNS>>();
            List<string> tbNameKeys = new List<string>();
            #region 从数据库中得到以架构名+.+表名为key，INFORMATION_SCHEMA_COLUMNS列表为结果传到tableInfo
            using (SqlConnection sqlCon = new SqlConnection(conStr))
            {
                try
                {
                    sqlCon.Open();
                    using (SqlCommand cmd = new SqlCommand("select * from INFORMATION_SCHEMA.TABLES",sqlCon))
                    {
                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            foreach(var line in reader)
                            {
                                System.Data.Common.DbDataRecord dbDataRecord = line as System.Data.Common.DbDataRecord;
                                //架构名+表名 作为 tableinfo的key
                                StringBuilder sb = new StringBuilder();
                                sb.Append(dbDataRecord.GetString(1));
                                sb.Append(".");
                                sb.Append(dbDataRecord.GetString(2));
                                tbNameKeys.Add(sb.ToString());
                            }
                        }
                        foreach(string key in tbNameKeys)
                        {
                            string[] rs = key.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                            string tableName = rs[1];
                            string schemaName = rs[0];
                            using (SqlCommand getColumnCMD = new SqlCommand("select * from INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @tableName and TABLE_SCHEMA = @schemaName order by ORDINAL_POSITION", sqlCon))
                            {
                                getColumnCMD.Parameters.AddWithValue("tableName", tableName);
                                getColumnCMD.Parameters.AddWithValue("schemaName", schemaName);
                                List<INFORMATION_SCHEMA_COLUMNS> tbList = new List<INFORMATION_SCHEMA_COLUMNS>();
                                using (SqlDataReader reader1 = getColumnCMD.ExecuteReader())
                                {
                                    foreach (var line1 in reader1)
                                    {
                                        System.Data.Common.DbDataRecord dbDataRecord1 = line1 as System.Data.Common.DbDataRecord;
                                        //在这里将所有的行创建为对象放入tableInfo
                                        INFORMATION_SCHEMA_COLUMNS tbInfo = new INFORMATION_SCHEMA_COLUMNS();
                                        tbInfo.TABLE_CATALOG = dbDataRecord1.GetString(0);
                                        tbInfo.TABLE_SCHEMA = dbDataRecord1.GetString(1);
                                        tbInfo.TABLE_NAME = dbDataRecord1.GetString(2);
                                        tbInfo.COLUMN_NAME = dbDataRecord1.GetString(3);
                                        tbInfo.ORDINAL_POSITION = dbDataRecord1.GetInt32(4);
                                        tbInfo.COLUMN_DEFAULT = dbDataRecord1.GetValue(5);
                                        if (dbDataRecord1.GetValue(6).Equals("NO"))
                                        {
                                            tbInfo.IS_NULLABLE = false;
                                        }
                                        else
                                        {
                                            tbInfo.IS_NULLABLE = true;
                                        }
                                        tbInfo.DATA_TYPE = dbDataRecord1.GetString(7);
                                        tbInfo.CHARACTER_MAXIMUM_LENGTH = dbDataRecord1.GetValue(8);
                                        //之后的以后补充
                                        tbList.Add(tbInfo);
                                    }
                                    tableInfo.Add(key, tbList);
                                }
                            }
                        }
                        
                    }
                }catch(SqlException sqlExc)
                {
                    //处理异常
                }
            }
            #endregion
            CreateClassFileInPath(tableInfo);

        }
        /// <summary>
        /// 将生成的数据库中表映射对象传入，生成c#代码
        /// </summary>
        /// <param name="tbInfo">映射对象</param>
        private void CreateClassFileInPath(Dictionary<string, List<INFORMATION_SCHEMA_COLUMNS>> tbInfo)
        {
            String DatabaseName;
            String SchemaName;
            String TableName;
            foreach(KeyValuePair<string,List<INFORMATION_SCHEMA_COLUMNS>> keyValue in tbInfo)
            {
                StringBuilder sb = new StringBuilder();
                DatabaseName = keyValue.Value[0].TABLE_CATALOG;
                SchemaName = keyValue.Value[0].TABLE_SCHEMA;
                TableName = keyValue.Value[0].TABLE_NAME;
                //在这里增加引用命名空间
                sb.AppendLine("using System;");
                sb.AppendLine(string.Format(namespaceFormat, DatabaseName, SchemaName));
                sb.AppendLine("{");
                sb.AppendLine(string.Format(classFormat, TableName));
                sb.AppendLine("\t{");
                foreach(INFORMATION_SCHEMA_COLUMNS isc in keyValue.Value)
                {
                    string type = getPropertyBySqlType(isc.DATA_TYPE);
                    sb.AppendLine(string.Format(fieldFormat, type, isc.COLUMN_NAME));
                    sb.AppendLine(string.Format(propertyFormat, type,isc.COLUMN_NAME));
                    sb.AppendLine("\t\t{");
                    sb.AppendLine(string.Format(getFormat, isc.COLUMN_NAME));
                    sb.AppendLine(string.Format(setFormat, isc.COLUMN_NAME));
                    sb.AppendLine("\t\t}");
                    //sb.Append(string.Format("public {0} {1} {get;set;}",type,isc.COLUMN_NAME));
                }
                sb.AppendLine("\t}");
                sb.AppendLine("}");
                saveClassInFileByPath(DatabaseName,SchemaName,TableName,sb.ToString());
                //将生成的类保存到文件中
                
            }
        }
        private void saveClassInFileByPath(string database,string schemaName,string tableName,string classStr)
        {
            String path = Path.Combine(codePath, database, schemaName);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            File.WriteAllText(Path.Combine(path, tableName + ".cs"), classStr);
        }
        private string getPropertyBySqlType(string type)
        {
            string rType;
            switch (type)
            {
                case "int":
                    rType = "int";
                    break;
                case "text":
                case "char":
                case "nchar":
                case "ntext":
                case "nvarchar":
                case "varchar":
                    rType = "string";
                    break;
                case "bigint":
                    rType = "long";
                    break;
                case "binary":
                case "image":
                case "varbinary":
                    rType = "byte[]";
                    break;
                case "bit":
                    rType = "bool";
                    break;
                case "datetime":
                case "smalldatetime":
                case "timestamp":
                    rType = "DateTime";
                    break;
                case "decimal":
                case "numeric":
                case "money":
                case "smallmoney":
                    rType = "decimal";
                    break;
                case "float":
                    rType = "double";
                    break;
                case "real":
                    rType = "Single";
                    break;

                case "smallint":
                    rType = "short";
                    break;
                case "tinyint":
                    rType = "byte";
                    break;
                case "variant":
                    rType = "object";
                    break;
                case "unique identifier":
                    rType = "Guid";
                    break;
                default:
                    throw new Exception("数据库数据类型转换成c#类型出错，错误类型为:" + type);
                    break;
            }
            return rType;
        }

        public class INFORMATION_SCHEMA_COLUMNS
        {
            /// <summary>
            /// 所属数据库名称
            /// </summary>
            public string TABLE_CATALOG { get; set; }
            /// <summary>
            /// 所属架构
            /// </summary>
            public string TABLE_SCHEMA { get; set; }
            /// <summary>
            /// 表名
            /// </summary>
            public string TABLE_NAME { get; set; }
            /// <summary>
            /// 列名
            /// </summary>
            public string COLUMN_NAME { get; set; }
            /// <summary>
            /// 列的顺序（根据这个排列生成的属性顺序）
            /// </summary>
            public int ORDINAL_POSITION { get; set; }
            /// <summary>
            /// 默认值（没有则为NULL）
            /// </summary>
            public object COLUMN_DEFAULT { get; set; }
            /// <summary>
            /// 可否为NULL（数据库里为NO和YES）
            /// </summary>
            public bool IS_NULLABLE { get; set; }
            /// <summary>
            /// 数据类型
            /// </summary>
            public string DATA_TYPE { get; set; }
            /// <summary>
            /// 最大长度
            /// </summary>
            public object CHARACTER_MAXIMUM_LENGTH { get; set; }
            public string CHARACTER_OCTET_LENGTH { get; set; }
            public string NUMERIC_PRECISION { get; set; }
            public string NUMERIC_PRECISION_RADIX { get; set; }
            public string NUMERIC_SCALE { get; set; }
            public string DATETIME_PRECISION { get; set; }
            public string CHARACTER_SET_CATALOG { get; set; }
            public string CHARACTER_SET_SCHEMA { get; set; }
            public string CHARACTER_SET_NAME { get; set; }
            public string COLLATION_CATALOG { get; set; }
            public string COLLATION_SCHEMA { get; set; }
            public string COLLATION_NAME { get; set; }
            public string DOMAIN_CATALOG { get; set; }
            public string DOMAIN_SCHEMA { get; set; }
            public string DOMAIN_NAME { get; set; }
        }
    }
}
