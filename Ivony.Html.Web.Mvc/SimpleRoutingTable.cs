﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web.Routing;
using System.Text.RegularExpressions;
using System.Web;
using Ivony.Fluent;
using System.Collections.Specialized;

namespace Ivony.Html.Web.Mvc
{
  public class SimpleRoutingTable : RouteBase
  {

    /// <summary>
    /// 获取请求的路由数据
    /// </summary>
    /// <param name="httpContext">HTTP 请求</param>
    /// <returns>路由数据</returns>
    public override RouteData GetRouteData( HttpContextBase httpContext )
    {
      var virtualPath = httpContext.Request.AppRelativeCurrentExecutionFilePath;

      var data = _rules
        .OrderBy( r => r.DynamicRouteKyes.Length )
        .Select( r => new
          {
            Rule = r,
            Values = r.GetRouteValues( virtualPath, httpContext.Request.QueryString ),
          } )
        .Where( i => i.Values != null )
        .FirstOrDefault();

      if ( data == null )
        return null;


      var routeData = new RouteData( this, Handler );

      foreach ( var pair in data.Values )
        routeData.Values.Add( pair.Key, pair.Value );


      routeData.DataTokens["RoutingRuleName"] = data.Rule.Name;

      return routeData;

    }


    /// <summary>
    /// 尝试从路由值创建虚拟路径
    /// </summary>
    /// <param name="requestContext">当前请求上下文</param>
    /// <param name="values">路由值</param>
    /// <returns>虚拟路径信息</returns>
    public override VirtualPathData GetVirtualPath( RequestContext requestContext, RouteValueDictionary values )
    {


      var _values = values.ToDictionary( pair => pair.Key, pair => pair.Value == null ? null : pair.Value.ToString(), StringComparer.OrdinalIgnoreCase );
      var keySet = new HashSet<string>( _values.Keys, StringComparer.OrdinalIgnoreCase );


      var candidateRules = _rules
        .Where( r => keySet.IsSupersetOf( r.RouteKeys ) )                      //所有路由键都必须匹配
        .Where( r => keySet.IsSubsetOf( r.AllKeys ) || !r.LimitedQueries )     //所有路由键和查询字符串键必须能涵盖要设置的键。
        .Where( r => r.IsMatch( _values ) );                                   //必须满足路由规则所定义的路由数据。

      if ( !candidateRules.Any() )
        return null;


      var bestRule = BestRule( candidateRules );

      var virtualPath = bestRule.CreateVirtualPath( _values );

      if ( MvcCompatible )
        virtualPath = virtualPath.Substring( 2 );


      var data = new VirtualPathData( this, virtualPath );

      data.DataTokens["RoutingRuleName"] = bestRule.Name;

      return data;
    }


    /// <summary>
    /// 添加一个路由规则
    /// </summary>
    /// <param name="name"></param>
    /// <param name="urlPattern"></param>
    /// <param name="routeValues"></param>
    /// <param name="defaultValues"></param>
    /// <param name="queryKeys"></param>
    public void AddRule( string name, string urlPattern, IDictionary<string, string> routeValues, string[] queryKeys )
    {
      AddRule( name, urlPattern, routeValues, queryKeys, false );
    }


    public void AddRule( string name, string urlPattern, IDictionary<string, string> routeValues, string[] queryKeys, bool limitedQueries )
    {
      var rule = new SimpleRoutingRule( this, name, urlPattern, routeValues, queryKeys, limitedQueries );

      AddRule( rule );
    }

    /// <summary>
    /// 添加一个路由规则
    /// </summary>
    /// <param name="rule">路由规则</param>
    protected void AddRule( SimpleRoutingRule rule )
    {

      {
        var conflictRule = _rules
          .Where( r => r.RouteKeys.Length == rule.RouteKeys.Length )                    //若通过RouteKey多寡无法区分
          .Where( r => r.DynamicRouteKyes.Length == rule.DynamicRouteKyes.Length )      //若通过动态路径段多寡也无法区分
          .Where( r => !SimpleRoutingRule.Mutex( r, rule ) )                            //若与现存规则不互斥
          .FirstOrDefault();

        if ( conflictRule != null )
          throw new InvalidOperationException( string.Format( "添加规则\"{0}\"失败，路由表中已经存在一条可能冲突的规则：\"{1}\"", rule.Name, conflictRule.Name ) );
      }


      {
        var conflictRule = _rules
          .Where( r => r.Paragraphes.Length == rule.Paragraphes.Length )                //若路径段长度一致
          .Where( r => r.StaticPrefix.EqualsIgnoreCase( rule.StaticPrefix ) )           //若静态段也一致
          .FirstOrDefault();


        if ( conflictRule != null )
          throw new InvalidOperationException( string.Format( "添加规则\"{0}\"失败，路由表中已经存在一条可能冲突的规则：\"{1}\"", rule.Name, conflictRule.Name ) );
      }

      _rules.Add( rule );
    }


    private SimpleRoutingRule BestRule( IEnumerable<SimpleRoutingRule> candidateRules )
    {

      //满足最多静态值的被优先考虑
      var maxConstraints = candidateRules.Max( r => r.StaticRouteValues.Count );
      candidateRules = candidateRules.Where( r => r.StaticRouteValues.Count == maxConstraints );

      if ( candidateRules.IsSingle() )
        return candidateRules.Single();


      //拥有最多路由键的被优先考虑
      var maxRouteKeys = candidateRules.Max( r => r.RouteKeys.Length );
      candidateRules = candidateRules.Where( r => r.RouteKeys.Length == maxRouteKeys );

      if ( candidateRules.IsSingle() )
        return candidateRules.Single();


      //拥有最少动态参数的被优先考虑
      var minDynamics = candidateRules.Min( p => p.DynamicRouteKyes.Length );
      candidateRules = candidateRules.Where( r => r.DynamicRouteKyes.Length == minDynamics );

      if ( candidateRules.IsSingle() )
        return candidateRules.Single();

      else
        return null;

    }



    /// <summary>
    /// 创建一个简单路由表实例
    /// </summary>
    /// <param name="handler"></param>
    /// <param name="mvcCompatible"></param>
    public SimpleRoutingTable( IRouteHandler handler, bool mvcCompatible )
    {
      Handler = handler;
      MvcCompatible = mvcCompatible;
      UrlEncoding = Encoding.UTF8;


    }



    /// <summary>
    /// 是否产生MVC兼容的虚拟路径（去除~/）
    /// </summary>
    public bool MvcCompatible
    {
      get;
      private set;
    }




    private ICollection<SimpleRoutingRule> _rules = new List<SimpleRoutingRule>();

    /// <summary>
    /// 路由表中定义的路由规则
    /// </summary>
    public SimpleRoutingRule[] Rules
    {
      get
      {
        return _rules.ToArray();
      }
    }



    public IRouteHandler Handler { get; private set; }

    public Encoding UrlEncoding { get; private set; }
  }



  /// <summary>
  /// 简单路由规则
  /// </summary>
  public class SimpleRoutingRule
  {

    public const string staticParagraphPattern = @"(?<paragraph>[\p{Lu}\p{Ll}\p{Nd}-]+)";
    public const string dynamicParagraphPattern = @"(?<paragraph>\{[\p{Lu}\p{Ll}\p{Nd}]+\})";
    public static readonly string urlPattern = @"(^~/$)|(^~((/{static}(/{static})*(/{dynamic})*)|((/{dynamic})+))$)".Replace( "{static}", staticParagraphPattern ).Replace( "{dynamic}", dynamicParagraphPattern );

    private static readonly Regex urlPatternRegex = new Regex( urlPattern, RegexOptions.Compiled );


    /// <summary>
    /// 创建一个简单路由规则
    /// </summary>
    /// <param name="routingTable"></param>
    /// <param name="name"></param>
    /// <param name="urlPattern"></param>
    /// <param name="routeValues"></param>
    /// <param name="defaultValues"></param>
    /// <param name="queryKeys"></param>
    internal SimpleRoutingRule( SimpleRoutingTable routingTable, string name, string urlPattern, IDictionary<string, string> routeValues, string[] queryKeys, bool limitedQueries )
    {
      RoutingTable = routingTable;

      Name = name;

      LimitedQueries = limitedQueries;



      var match = urlPatternRegex.Match( urlPattern );

      if ( !match.Success )
        throw new FormatException( "URL模式格式不正确" );

      _paragraphes = match.Groups["paragraph"].Captures.Cast<Capture>().Select( c => c.Value ).ToArray();

      _urlPattern = urlPattern;
      _staticValues = new Dictionary<string, string>( routeValues, StringComparer.OrdinalIgnoreCase );


      _routeKeys = new HashSet<string>( _staticValues.Keys, StringComparer.OrdinalIgnoreCase );

      _dynamics = new HashSet<string>( _paragraphes.Where( p => p.StartsWith( "{" ) && p.EndsWith( "}" ) ).Select( p => p.Substring( 1, p.Length - 2 ) ), StringComparer.OrdinalIgnoreCase );



      foreach ( var key in _dynamics )
      {
        if ( _routeKeys.Contains( key ) )
          throw new FormatException( "URL模式格式不正确，包含重复的动态参数名或动态参数名与预设路由键重复" );


        _routeKeys.Add( key );
      }

      if ( _routeKeys.Intersect( queryKeys, StringComparer.OrdinalIgnoreCase ).Any() )
        throw new FormatException( "URL模式格式不正确，动态参数或预设路由键与可选查询字符串名重复" );

      _queryKeys = new HashSet<string>( queryKeys, StringComparer.OrdinalIgnoreCase );

      _allKeys = new HashSet<string>( _routeKeys, StringComparer.OrdinalIgnoreCase );
      _allKeys.UnionWith( _queryKeys );

    }


    public string Name
    {
      get;
      private set;
    }



    public bool LimitedQueries
    {
      get;
      private set;
    }


    private string[] _paragraphes;

    /// <summary>
    /// 获取所有路径段
    /// </summary>
    public string[] Paragraphes
    {
      get { return _paragraphes.ToArray(); }
    }


    private HashSet<string> _routeKeys;

    /// <summary>
    /// 获取所有路由键（包括静态和动态的）
    /// </summary>
    /// <remarks>
    /// 路由键的值会作为虚拟路径的一部分
    /// </remarks>
    public string[] RouteKeys
    {
      get { return _routeKeys.ToArray(); }
    }



    private HashSet<string> _queryKeys;

    /// <summary>
    /// 获取所有查询键
    /// </summary>
    /// <remarks>
    /// 构造虚拟路径时，查询键都是可选的。
    /// 查询键的值会被产生为查询字符串。
    /// </remarks>
    public string[] QueryKeys
    {
      get { return _queryKeys.ToArray(); }
    }


    private HashSet<string> _allKeys;

    /// <summary>
    /// 获取所有键（包括路由键和查询键）
    /// </summary>
    public string[] AllKeys
    {
      get
      {
        return _allKeys.ToArray();
      }
    }


    private HashSet<string> _dynamics;

    /// <summary>
    /// 获取所有动态路由键
    /// </summary>
    /// <remarks>
    /// 动态路由键的值不能包含特殊字符
    /// </remarks>
    public string[] DynamicRouteKyes
    {
      get { return _dynamics.ToArray(); }
    }


    private string _prefix;

    /// <summary>
    /// 获取URL模式的静态前缀
    /// </summary>
    public string StaticPrefix
    {
      get
      {
        if ( _prefix == null )
        {
          var dynamicStarts = _urlPattern.IndexOf( '{' );
          if ( dynamicStarts < 0 )
            _prefix = _urlPattern;
          else
            _prefix = _urlPattern.Substring( 0, dynamicStarts - 1 );
        }

        return _prefix;
      }
    }


    private string _urlPattern;

    /// <summary>
    /// 获取整个URL模式
    /// </summary>
    public string UrlPattern
    {
      get { return _urlPattern; }
    }


    private IDictionary<string, string> _staticValues;

    /// <summary>
    /// 获取所有的静态值
    /// </summary>
    public IDictionary<string, string> StaticRouteValues
    {
      get { return new Dictionary<string, string>( _staticValues ); }
    }


    public string CreateVirtualPath( IDictionary<string, string> routeValues )
    {

      var builder = new StringBuilder( StaticPrefix );

      foreach ( var key in DynamicRouteKyes )
      {
        var value = routeValues[key];

        if ( string.IsNullOrEmpty( value ) )
          throw new ArgumentException( "作为动态路径的路由值不能包含空引用或空字符串", "routeValues" );


        if ( value.Contains( '/' ) )
          throw new ArgumentException( "作为动态路径的路由值不能包含路径分隔符 '/'", "routeValues" );

        //value = HttpUtility.UrlEncode( value, RoutingTable.UrlEncoding );

        builder.Append( "/" + value );

      }


      bool isAppendedQueryStartSymbol = false;

      foreach ( var key in QueryKeys )
      {

        if ( routeValues.ContainsKey( key ) )
        {

          var value = routeValues[key];

          if ( !isAppendedQueryStartSymbol )
            builder.Append( '?' );
          else
            builder.Append( '&' );

          isAppendedQueryStartSymbol = true;

          builder.Append( HttpUtility.UrlEncode( key ) );
          builder.Append( '=' );
          builder.Append( HttpUtility.UrlEncode( routeValues[key] ) );

        }
      }


      return builder.ToString();
    }


    public SimpleRoutingTable RoutingTable
    {
      get;
      private set;
    }


    /// <summary>
    /// 检查指定的路由值是否满足约束
    /// </summary>
    /// <param name="values">路由值</param>
    /// <returns>是否满足路由规则的约束</returns>
    public bool IsMatch( IDictionary<string, string> values )
    {

      if ( values == null )
        throw new ArgumentNullException( "values" );


      foreach ( var key in _staticValues.Keys )
      {
        string val;

        if ( !values.TryGetValue( key, out val ) )
          return false;

        if ( !_staticValues[key].EqualsIgnoreCase( val ) )
          return false;
      }

      return true;
    }



    /// <summary>
    /// 检查两个路由规则是否互斥。
    /// </summary>
    /// <param name="rule1">路由规则1</param>
    /// <param name="rule2">路由规则2</param>
    /// <returns></returns>
    public static bool Mutex( SimpleRoutingRule rule1, SimpleRoutingRule rule2 )
    {

      var KeySet = new HashSet<string>( rule1.StaticRouteValues.Keys, StringComparer.OrdinalIgnoreCase );
      var KeySet2 = new HashSet<string>( rule2.StaticRouteValues.Keys, StringComparer.OrdinalIgnoreCase );

      KeySet.IntersectWith( KeySet2 );

      return KeySet.Any( key => !rule1.StaticRouteValues[key].EqualsIgnoreCase( rule2.StaticRouteValues[key] ) );
    }


    /// <summary>
    /// 比较两个路由规则约束是否一致
    /// </summary>
    /// <param name="rule">要比较的路由规则</param>
    /// <returns>两个规则的约束是否一致</returns>
    public bool EqualsConstraints( SimpleRoutingRule rule )
    {

      if ( rule == null )
        throw new ArgumentNullException( "rule" );


      if ( rule.StaticRouteValues.Count != StaticRouteValues.Count )
        return false;

      return IsMatch( rule.StaticRouteValues );

    }

    public virtual IDictionary<string, string> GetRouteValues( string virtualPath, NameValueCollection queryString )
    {

      if ( virtualPath == null )
        throw new ArgumentNullException( "virtualPath" );

      if ( !VirtualPathUtility.IsAppRelative( virtualPath ) )
        throw new ArgumentException( "必须是相对于应用程序根的路径" );

      var queryKeySet = new HashSet<string>( _queryKeys );

      if ( LimitedQueries && !queryKeySet.IsSupersetOf( queryString.AllKeys ) )
        return null;


      virtualPath = Regex.Replace( virtualPath, "/+", "/" );//将连续的/替换成单独的/

      virtualPath = virtualPath.Substring( 2 );



      var pathParagraphs = virtualPath.Split( '/' );

      if ( virtualPath == "" )
        pathParagraphs = new string[0];


      if ( pathParagraphs.Length != Paragraphes.Length )
        return null;


      var values = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );



      foreach ( var pair in _staticValues )
        values.Add( pair.Key, pair.Value );

      for ( int i = 0; i < pathParagraphs.Length; i++ )
      {

        var paragraph = Paragraphes[i];

        if ( !paragraph.StartsWith( "{" ) )
        {
          if ( !pathParagraphs[i].EqualsIgnoreCase( paragraph ) )
            return null;
        }
        else
        {
          var name = paragraph.Substring( 1, paragraph.Length - 2 );
          values.Add( name, pathParagraphs[i] );
        }
      }


      foreach ( var key in queryString.AllKeys )
        values.Add( key, queryString[key] );


      return values;
    }


    public override string ToString()
    {
      return UrlPattern;
    }

  }
}
