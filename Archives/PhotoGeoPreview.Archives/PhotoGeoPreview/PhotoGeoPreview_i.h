

/* this ALWAYS GENERATED file contains the definitions for the interfaces */


 /* File created by MIDL compiler version 8.01.0628 */
/* at Tue Jan 19 12:14:07 2038
 */
/* Compiler settings for PhotoGeoPreview.idl:
    Oicf, W1, Zp8, env=Win64 (32b run), target_arch=AMD64 8.01.0628 
    protocol : all , ms_ext, c_ext, robust
    error checks: allocation ref bounds_check enum stub_data 
    VC __declspec() decoration level: 
         __declspec(uuid()), __declspec(selectany), __declspec(novtable)
         DECLSPEC_UUID(), MIDL_INTERFACE()
*/
/* @@MIDL_FILE_HEADING(  ) */



/* verify that the <rpcndr.h> version is high enough to compile this file*/
#ifndef __REQUIRED_RPCNDR_H_VERSION__
#define __REQUIRED_RPCNDR_H_VERSION__ 500
#endif

#include "rpc.h"
#include "rpcndr.h"

#ifndef __RPCNDR_H_VERSION__
#error this stub requires an updated version of <rpcndr.h>
#endif /* __RPCNDR_H_VERSION__ */


#ifndef __PhotoGeoPreview_i_h__
#define __PhotoGeoPreview_i_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

#ifndef DECLSPEC_XFGVIRT
#if defined(_CONTROL_FLOW_GUARD_XFG)
#define DECLSPEC_XFGVIRT(base, func) __declspec(xfg_virtual(base, func))
#else
#define DECLSPEC_XFGVIRT(base, func)
#endif
#endif

/* Forward Declarations */ 

#ifndef __PhotoGeoPreviewHandler_FWD_DEFINED__
#define __PhotoGeoPreviewHandler_FWD_DEFINED__

#ifdef __cplusplus
typedef class PhotoGeoPreviewHandler PhotoGeoPreviewHandler;
#else
typedef struct PhotoGeoPreviewHandler PhotoGeoPreviewHandler;
#endif /* __cplusplus */

#endif 	/* __PhotoGeoPreviewHandler_FWD_DEFINED__ */


/* header files for imported files */
#include "oaidl.h"
#include "ocidl.h"
#include "shobjidl.h"

#ifdef __cplusplus
extern "C"{
#endif 



#ifndef __PhotoGeoPreviewLib_LIBRARY_DEFINED__
#define __PhotoGeoPreviewLib_LIBRARY_DEFINED__

/* library PhotoGeoPreviewLib */
/* [version][uuid] */ 


EXTERN_C const IID LIBID_PhotoGeoPreviewLib;

EXTERN_C const CLSID CLSID_PhotoGeoPreviewHandler;

#ifdef __cplusplus

class DECLSPEC_UUID("8A3D6B10-4F2C-4E1A-9D3B-7C5E8F1A2B3C")
PhotoGeoPreviewHandler;
#endif
#endif /* __PhotoGeoPreviewLib_LIBRARY_DEFINED__ */

/* Additional Prototypes for ALL interfaces */

/* end of Additional Prototypes */

#ifdef __cplusplus
}
#endif

#endif


